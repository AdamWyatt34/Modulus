using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.HealthChecks;
using Modulus.Messaging.Tests.Fixtures;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.HealthChecks;

public class ModulusMessagingHealthCheckTests
{
    private static HealthCheckContext Context() => new()
    {
        Registration = new HealthCheckRegistration(
            "test",
            _ => null!,
            failureStatus: HealthStatus.Unhealthy,
            tags: null),
    };

    private sealed class ProbeTransport(TransportHealth health) : IMessageTransport, ITransportHealthProbe
    {
        public Exception? ProbeFailure { get; set; }

        public ValueTask<TransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
            => ProbeFailure is not null ? throw ProbeFailure : ValueTask.FromResult(health);

        public Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StartConsumingAsync(
            IReadOnlyList<TransportSubscription> subscriptions,
            Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopConsumingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingOutboxStore(Func<int> pendingCount) : IOutboxStore
    {
        public Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, int maxAttempts, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);

        public Task<int> CountPending(int maxAttempts, CancellationToken cancellationToken = default)
            => Task.FromResult(pendingCount());

        public Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkAsFailed(Guid messageId, string error, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task TransportCheck_TransportWithoutProbe_ReportsHealthyWithNote()
    {
        var check = new TransportHealthCheck(new FakeMessageTransport());

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldContain("no health probe");
    }

    [Fact]
    public async Task TransportCheck_HealthyProbe_ReportsHealthy()
    {
        var check = new TransportHealthCheck(new ProbeTransport(new TransportHealth(true, "connected")));

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldBe("connected");
    }

    [Fact]
    public async Task TransportCheck_UnhealthyProbe_ReportsFailureStatus()
    {
        var check = new TransportHealthCheck(new ProbeTransport(new TransportHealth(false, "broker down")));

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("broker down");
    }

    [Fact]
    public async Task TransportCheck_ProbeThrows_ReportsFailureStatusNotException()
    {
        var transport = new ProbeTransport(new TransportHealth(true))
        {
            ProbeFailure = new InvalidOperationException("boom"),
        };
        var check = new TransportHealthCheck(transport);

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Exception.ShouldBeOfType<InvalidOperationException>();
    }

    private static OutboxBacklogHealthCheck BacklogCheck(
        IOutboxStore? store,
        int degraded = 100,
        int unhealthy = 1000)
    {
        var services = new ServiceCollection();
        if (store is not null)
            services.AddScoped<IOutboxStore>(_ => store);
        var provider = services.BuildServiceProvider();

        return new OutboxBacklogHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MessagingOptions(),
            new ModulusMessagingHealthCheckOptions
            {
                OutboxDegradedThreshold = degraded,
                OutboxUnhealthyThreshold = unhealthy,
            });
    }

    [Fact]
    public async Task BacklogCheck_NoOutboxRegistered_ReportsHealthy()
    {
        var result = await BacklogCheck(store: null).CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldContain("No outbox");
    }

    [Theory]
    [InlineData(0, HealthStatus.Healthy)]
    [InlineData(99, HealthStatus.Healthy)]
    [InlineData(100, HealthStatus.Degraded)]
    [InlineData(999, HealthStatus.Degraded)]
    [InlineData(1000, HealthStatus.Unhealthy)]
    public async Task BacklogCheck_ThresholdBoundaries_MapToExpectedStatus(int pending, HealthStatus expected)
    {
        var check = BacklogCheck(new CountingOutboxStore(() => pending));

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(expected);
        result.Data["pending"].ShouldBe(pending);
    }

    [Fact]
    public async Task BacklogCheck_StoreThrows_ReportsFailureStatusNotException()
    {
        var check = BacklogCheck(new CountingOutboxStore(() => throw new InvalidOperationException("db down")));

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Exception.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void AddModulusMessaging_RegistersBothChecksWithReadyTag()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageTransport>(new FakeMessageTransport());
        services.AddSingleton(new MessagingOptions());
        services.AddHealthChecks().AddModulusMessaging();

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>()
            .Value;

        var registrations = options.Registrations.ToList();
        registrations.Select(r => r.Name)
            .ShouldBe(["modulus_messaging_transport", "modulus_messaging_outbox"], ignoreOrder: true);
        registrations.ShouldAllBe(r => r.Tags.Contains("ready") && r.Tags.Contains("messaging"));
    }

    [Fact]
    public void AddModulusMessaging_UnhealthyBelowDegraded_Throws()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddHealthChecks().AddModulusMessaging(o =>
            {
                o.OutboxDegradedThreshold = 500;
                o.OutboxUnhealthyThreshold = 100;
            }));
    }
}
