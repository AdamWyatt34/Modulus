using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.RabbitMq.IntegrationTests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.RabbitMq.IntegrationTests;

// Broker-native delayed redelivery (ConsumerRetryMode.Broker: TTL retry queue + DLX back to
// the work queue) and scheduled publish (per-event-type TTL holding queue) against a real
// broker.
[Collection(RabbitMqCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RabbitMqBrokerRedeliveryIntegrationTests(RabbitMqContainerFixture rabbitMq)
{
    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(30);

    private ServiceCollection BuildServices(string endpointName, Action<MessagingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.RabbitMq;
            options.ConnectionString = rabbitMq.ConnectionString;
            options.EndpointName = endpointName;
            options.Assemblies.Add(typeof(RoundTripEvent).Assembly);
            configure?.Invoke(options);
        });
        services.AddModulusRabbitMqTransport();
        return services;
    }

    private static async Task<StartedHost> StartHost(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var started = new List<IHostedService>();

        foreach (var hostedService in provider.GetServices<IHostedService>())
        {
            if (hostedService is not OutboxProcessor)
            {
                await hostedService.StartAsync(CancellationToken.None);
                started.Add(hostedService);
            }
        }

        return new StartedHost(provider, started);
    }

    private sealed record StartedHost(ServiceProvider Provider, List<IHostedService> Started) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var i = Started.Count - 1; i >= 0; i--)
                await Started[i].StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    private static async Task WaitFor(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + BrokerTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Condition not met within {BrokerTimeout.TotalSeconds}s: {because}");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task FailingHandler_InBrokerMode_IsRedeliveredViaRetryQueue_AndSucceeds()
    {
        BrokerRetryHandler.Reset();
        await using var host = await StartHost(BuildServices("it-broker-retry", options =>
        {
            options.ConsumerRetryMode = ConsumerRetryMode.Broker;
            options.ConsumerRetry.MaxAttempts = 3;
            options.ConsumerRetry.InitialInterval = TimeSpan.FromMilliseconds(500);
            options.ConsumerRetry.MaxInterval = TimeSpan.FromMilliseconds(500);
            options.ConsumerRetry.IntervalIncrement = TimeSpan.Zero;
        }));

        using var scope = host.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var eventId = Guid.NewGuid();
        await messageBus.Publish(new BrokerRetryEvent { EventId = eventId, Value = 7 });

        await WaitFor(
            () => !BrokerRetryHandler.Handled.IsEmpty,
            "the broker-scheduled redelivery should reach the handler and succeed");

        BrokerRetryHandler.Attempts.ShouldBe(2);
        BrokerRetryHandler.Handled.TryPeek(out var received).ShouldBeTrue();
        received!.EventId.ShouldBe(eventId);
        received.Value.ShouldBe(7);
    }

    [Fact]
    public async Task ScheduledPublish_IsHeldByTheBroker_UntilTheEnqueueTime()
    {
        ScheduledPublishHandler.Handled.Clear();
        await using var host = await StartHost(BuildServices("it-scheduled"));

        using var scope = host.Provider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var publishedAtUtc = DateTime.UtcNow;
        var delay = TimeSpan.FromSeconds(2);
        await messageBus.PublishScheduled(
            new ScheduledPublishEvent { Value = 11 },
            DateTimeOffset.UtcNow + delay);

        await WaitFor(
            () => !ScheduledPublishHandler.Handled.IsEmpty,
            "the scheduled message should be released to the handler after the delay");

        ScheduledPublishHandler.Handled.TryPeek(out var entry).ShouldBeTrue();
        entry.Event.Value.ShouldBe(11);
        // Allow generous slack for container clocks; the point is it did not arrive immediately.
        (entry.ReceivedAtUtc - publishedAtUtc).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
    }
}
