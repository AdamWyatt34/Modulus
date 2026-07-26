using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Tests.Fixtures;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Dispatch;

// Broker-native retry: one handler pass per delivery, attempt count in the envelope headers,
// Retry handed back to the transport instead of sleeping in process.
public class ConsumerDispatcherBrokerRetryTests
{
    private static MessagingOptions BrokerRetryOptions(int maxAttempts = 3, string? endpointName = null) => new()
    {
        ConsumerRetryMode = ConsumerRetryMode.Broker,
        EndpointName = endpointName,
        ConsumerRetry = new RetryPolicyOptions
        {
            MaxAttempts = maxAttempts,
            InitialInterval = TimeSpan.Zero,
            MaxInterval = TimeSpan.Zero,
            IntervalIncrement = TimeSpan.Zero,
        },
    };

    private static ConsumerDispatcher BuildDispatcher(IServiceCollection services, MessagingOptions options)
    {
        var provider = services.BuildServiceProvider();
        return new ConsumerDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MessageTypeRegistry([typeof(TestOrderCreatedEvent).Assembly]),
            NullLogger<ConsumerDispatcher>.Instance,
            options,
            new MessagingMetrics(meterFactory: null));
    }

    private static TransportEnvelope EnvelopeFor(
        TestOrderCreatedEvent @event,
        int? attempt = null,
        string? targetEndpoint = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attempt is { } a)
            headers["modulus-delivery-attempt"] = a.ToString();
        if (targetEndpoint is not null)
            headers["modulus-redeliver-endpoint"] = targetEndpoint;

        return new TransportEnvelope(
            MessageTypeRegistry.GetStableName(typeof(TestOrderCreatedEvent)),
            @event.EventId,
            @event.CorrelationId,
            @event.OccurredOn,
            MessageSerializer.Serialize(@event, typeof(TestOrderCreatedEvent)))
        {
            Headers = headers.Count > 0 ? headers : null,
        };
    }

    [Fact]
    public async Task Failure_with_attempts_remaining_returns_Retry_after_a_single_pass()
    {
        var handler = new FlakyOrderCreatedHandler(failuresBeforeSuccess: int.MaxValue);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services, BrokerRetryOptions(maxAttempts: 3));

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "T" }),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Retry);
        handler.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Failure_on_the_final_attempt_dead_letters()
    {
        var handler = new FlakyOrderCreatedHandler(failuresBeforeSuccess: int.MaxValue);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services, BrokerRetryOptions(maxAttempts: 3));

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 2, CustomerName = "T" }, attempt: 3),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.DeadLetter);
        handler.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Success_on_a_redelivered_attempt_acknowledges()
    {
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services, BrokerRetryOptions());

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 3, CustomerName = "T" }, attempt: 2),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.HandledEvents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Retry_releases_the_dispatch_reservation_so_the_redelivery_executes_immediately()
    {
        var inbox = new FakeInboxStore();
        var handler = new FlakyOrderCreatedHandler(failuresBeforeSuccess: 1);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        services.AddSingleton<IInboxStore>(inbox);
        var dispatcher = BuildDispatcher(services, BrokerRetryOptions(maxAttempts: 3));

        var @event = new TestOrderCreatedEvent { OrderId = 4, CustomerName = "T" };

        var first = await dispatcher.DispatchAsync(EnvelopeFor(@event), CancellationToken.None);
        first.ShouldBe(MessageDispatchResult.Retry);

        // The redelivery arrives well inside ConsumerReservationTimeout; had the failed
        // dispatch's reservation not been released, this would throw
        // InboxReservationPendingException internally and burn the attempt.
        var second = await dispatcher.DispatchAsync(EnvelopeFor(@event, attempt: 2), CancellationToken.None);

        second.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.Attempts.ShouldBe(2);
        handler.HandledEvents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_foreign_endpoints_redelivery_copy_is_acknowledged_without_dispatch()
    {
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services, BrokerRetryOptions(endpointName: "billing"));

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 5, CustomerName = "T" }, attempt: 2, targetEndpoint: "shipping"),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.HandledEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_own_endpoints_redelivery_copy_dispatches_normally()
    {
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services, BrokerRetryOptions(endpointName: "billing"));

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 6, CustomerName = "T" }, attempt: 2, targetEndpoint: "billing"),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.HandledEvents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task InProcess_mode_never_returns_Retry()
    {
        var handler = new FlakyOrderCreatedHandler(failuresBeforeSuccess: int.MaxValue);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var options = BrokerRetryOptions(maxAttempts: 2);
        options.ConsumerRetryMode = ConsumerRetryMode.InProcess;
        var dispatcher = BuildDispatcher(services, options);

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 7, CustomerName = "T" }),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.DeadLetter);
        handler.Attempts.ShouldBe(2);
    }
}
