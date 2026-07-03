using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Tests.Fixtures;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Dispatch;

// Direct unit tests for the consumer pipeline — no bus, no hosted services, no waits.
public class ConsumerDispatcherTests
{
    private static MessagingOptions FastRetryOptions(int maxAttempts = 1) => new()
    {
        ConsumerRetry = new RetryPolicyOptions
        {
            MaxAttempts = maxAttempts,
            InitialInterval = TimeSpan.Zero,
            MaxInterval = TimeSpan.Zero,
            IntervalIncrement = TimeSpan.Zero,
        },
    };

    private static ConsumerDispatcher BuildDispatcher(
        IServiceCollection services,
        MessagingOptions? options = null)
    {
        var provider = services.BuildServiceProvider();
        return new ConsumerDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MessageTypeRegistry([typeof(TestOrderCreatedEvent).Assembly]),
            NullLogger<ConsumerDispatcher>.Instance,
            options ?? FastRetryOptions());
    }

    private static TransportEnvelope EnvelopeFor(TestOrderCreatedEvent @event) => new(
        MessageTypeRegistry.GetStableName(typeof(TestOrderCreatedEvent)),
        @event.EventId,
        @event.CorrelationId,
        @event.OccurredOn,
        MessageSerializer.Serialize(@event, typeof(TestOrderCreatedEvent)));

    [Fact]
    public async Task Dispatch_RegisteredHandler_ReceivesEventWithAllProperties()
    {
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services);

        var @event = new TestOrderCreatedEvent
        {
            OrderId = 99,
            CustomerName = "Direct",
            CorrelationId = "corr-123"
        };

        var result = await dispatcher.DispatchAsync(EnvelopeFor(@event), CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.HandledEvents.Count.ShouldBe(1);
        handler.HandledEvents[0].OrderId.ShouldBe(99);
        handler.HandledEvents[0].CustomerName.ShouldBe("Direct");
        handler.HandledEvents[0].EventId.ShouldBe(@event.EventId);
        handler.HandledEvents[0].CorrelationId.ShouldBe("corr-123");
    }

    [Fact]
    public async Task Dispatch_MultipleHandlers_AllInvoked()
    {
        // The old MassTransit adapter resolved a single handler via GetRequiredService,
        // silently dropping all but the last registration. The dispatcher invokes every handler.
        var first = new TestOrderCreatedHandler();
        var second = new SecondOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(first);
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(second);
        var dispatcher = BuildDispatcher(services);

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "Both" }),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        first.HandledEvents.Count.ShouldBe(1);
        second.HandledEvents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dispatch_NoInboxRegistered_FallsThroughToDirectExecution()
    {
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services);

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "NoInbox" }),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.HandledEvents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dispatch_WithInbox_SecondDeliveryIsSkipped()
    {
        var handler = new TestOrderCreatedHandler();
        var inbox = new FakeInboxStore();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        services.AddSingleton<IInboxStore>(inbox);
        var dispatcher = BuildDispatcher(services);

        var envelope = EnvelopeFor(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "Dedup" });

        (await dispatcher.DispatchAsync(envelope, CancellationToken.None)).ShouldBe(MessageDispatchResult.Acknowledge);
        (await dispatcher.DispatchAsync(envelope, CancellationToken.None)).ShouldBe(MessageDispatchResult.Acknowledge);

        handler.HandledEvents.Count.ShouldBe(1);
        inbox.ProcessedConsumers.Count.ShouldBe(1);
        inbox.ProcessedConsumers[0].HandlerName.ShouldBe(nameof(TestOrderCreatedHandler));
    }

    [Fact]
    public async Task Dispatch_HandlerFailsOnce_RetriesAndSucceeds()
    {
        var handler = new FlakyOrderCreatedHandler(failuresBeforeSuccess: 1);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services, FastRetryOptions(maxAttempts: 3));

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "Flaky" }),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.Attempts.ShouldBe(2);
        handler.HandledEvents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dispatch_HandlerAlwaysFails_DeadLettersAfterMaxAttempts()
    {
        var handler = new FlakyOrderCreatedHandler(failuresBeforeSuccess: int.MaxValue);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services, FastRetryOptions(maxAttempts: 3));

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "Doomed" }),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.DeadLetter);
        handler.Attempts.ShouldBe(3);
    }

    [Fact]
    public async Task Dispatch_RetryWithInbox_SucceededHandlerNotReExecuted()
    {
        // First attempt: handler A succeeds (recorded), handler B throws.
        // Second attempt: A is skipped via the inbox, B succeeds.
        var succeeding = new TestOrderCreatedHandler();
        var flaky = new FlakyOrderCreatedHandler(failuresBeforeSuccess: 1);
        var inbox = new FakeInboxStore();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(succeeding);
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(flaky);
        services.AddSingleton<IInboxStore>(inbox);
        var dispatcher = BuildDispatcher(services, FastRetryOptions(maxAttempts: 3));

        var result = await dispatcher.DispatchAsync(
            EnvelopeFor(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "Partial" }),
            CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        succeeding.HandledEvents.Count.ShouldBe(1);
        flaky.HandledEvents.Count.ShouldBe(1);
        inbox.ProcessedConsumers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Dispatch_ConcurrentDuplicateDeliveries_HandlerExecutesExactlyOnce()
    {
        // Two dispatches of the same envelope race: the reservation makes one the owner;
        // the other backs off until it sees the pair processed, then acknowledges.
        var handler = new SlowOrderCreatedHandler(TimeSpan.FromMilliseconds(150));
        var inbox = new FakeInboxStore();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        services.AddSingleton<IInboxStore>(inbox);
        var dispatcher = BuildDispatcher(services, new MessagingOptions
        {
            ConsumerRetry = new RetryPolicyOptions
            {
                MaxAttempts = 20,
                InitialInterval = TimeSpan.FromMilliseconds(50),
                MaxInterval = TimeSpan.FromMilliseconds(50),
                IntervalIncrement = TimeSpan.Zero,
            },
        });

        var envelope = EnvelopeFor(new TestOrderCreatedEvent { OrderId = 7, CustomerName = "Race" });

        var results = await Task.WhenAll(
            dispatcher.DispatchAsync(envelope, CancellationToken.None),
            dispatcher.DispatchAsync(envelope, CancellationToken.None));

        results.ShouldAllBe(r => r == MessageDispatchResult.Acknowledge);
        handler.HandledEvents.Count.ShouldBe(1);
        inbox.ProcessedConsumers.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dispatch_StaleReservation_IsTakenOverAndReExecuted()
    {
        // Simulates a crashed owner: the reservation exists but never completed. Once it
        // ages past the timeout, a redelivery takes it over and runs the handler.
        var handler = new TestOrderCreatedHandler();
        var inbox = new FakeInboxStore();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        services.AddSingleton<IInboxStore>(inbox);
        var dispatcher = BuildDispatcher(services);

        var @event = new TestOrderCreatedEvent { OrderId = 8, CustomerName = "Crashed" };
        (await inbox.TryReserve(@event.EventId, nameof(TestOrderCreatedHandler), TimeSpan.FromMinutes(5)))
            .ShouldBeTrue();
        inbox.AgeReservation(@event.EventId, nameof(TestOrderCreatedHandler), TimeSpan.FromMinutes(10));

        var result = await dispatcher.DispatchAsync(EnvelopeFor(@event), CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.HandledEvents.Count.ShouldBe(1);
        inbox.ProcessedConsumers.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dispatch_LiveForeignReservation_RetriesThenDeadLetters()
    {
        // A live reservation held by another delivery must not be acknowledged past — the
        // dispatch retries and, if the owner never completes, dead-letters (replayable).
        var handler = new TestOrderCreatedHandler();
        var inbox = new FakeInboxStore();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        services.AddSingleton<IInboxStore>(inbox);
        var dispatcher = BuildDispatcher(services, FastRetryOptions(maxAttempts: 3));

        var @event = new TestOrderCreatedEvent { OrderId = 9, CustomerName = "Contended" };
        (await inbox.TryReserve(@event.EventId, nameof(TestOrderCreatedHandler), TimeSpan.FromMinutes(5)))
            .ShouldBeTrue();

        var result = await dispatcher.DispatchAsync(EnvelopeFor(@event), CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.DeadLetter);
        handler.HandledEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispatch_UnknownMessageType_AcknowledgesWithoutDispatch()
    {
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services);

        var envelope = new TransportEnvelope(
            "Unknown.Type",
            Guid.NewGuid(),
            null,
            DateTime.UtcNow,
            "{}"u8.ToArray());

        var result = await dispatcher.DispatchAsync(envelope, CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.Acknowledge);
        handler.HandledEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispatch_UnreadableBody_DeadLettersWithoutRetry()
    {
        var handler = new FlakyOrderCreatedHandler(failuresBeforeSuccess: 0);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        var dispatcher = BuildDispatcher(services, FastRetryOptions(maxAttempts: 5));

        var envelope = new TransportEnvelope(
            MessageTypeRegistry.GetStableName(typeof(TestOrderCreatedEvent)),
            Guid.NewGuid(),
            null,
            DateTime.UtcNow,
            "not-json"u8.ToArray());

        var result = await dispatcher.DispatchAsync(envelope, CancellationToken.None);

        result.ShouldBe(MessageDispatchResult.DeadLetter);
        handler.Attempts.ShouldBe(0);
    }
}
