using System.Collections.Concurrent;
using System.Text.Json;
using Modulus.Messaging;
using Modulus.Messaging.Transports;
using Modulus.Testing.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Testing.Tests;

// Direct instantiation (bypassing DI) to exercise the transport's own delivery semantics in
// isolation — mirrors Modulus.Messaging.Tests.InMemory.InMemoryTransportRedeliveryTests, since
// TestMessageTransport is meant to behave identically to the library's internal transport.
public class TestMessageTransportTests
{
    private static MessagingOptions ZeroDelayRetryOptions() => new()
    {
        ConsumerRetryMode = ConsumerRetryMode.Broker,
        ConsumerRetry = new RetryPolicyOptions
        {
            MaxAttempts = 3,
            InitialInterval = TimeSpan.Zero,
            MaxInterval = TimeSpan.Zero,
            IntervalIncrement = TimeSpan.Zero,
        },
    };

    private static TransportEnvelope Envelope(string type = "Test.Event")
        => new(type, Guid.NewGuid(), null, DateTime.UtcNow, "{}"u8.ToArray());

    private static TransportEnvelope EventEnvelope(TestOrderPlacedEvent @event) => new(
        typeof(TestOrderPlacedEvent).FullName!,
        @event.EventId,
        @event.CorrelationId,
        @event.OccurredOn,
        JsonSerializer.SerializeToUtf8Bytes(@event));

    [Fact]
    public async Task Retry_result_redelivers_a_copy_with_the_incremented_attempt_header()
    {
        await using var transport = new TestMessageTransport(ZeroDelayRetryOptions());
        var deliveries = new ConcurrentQueue<TransportEnvelope>();

        await transport.StartConsumingAsync(
            [new TransportSubscription(typeof(object), "Test.Event")],
            (envelope, _) =>
            {
                deliveries.Enqueue(envelope);
                return Task.FromResult(deliveries.Count == 1
                    ? MessageDispatchResult.Retry
                    : MessageDispatchResult.Acknowledge);
            });

        await transport.PublishAsync(Envelope());

        await TestWait.WaitForConditionAsync(() => deliveries.Count == 2);
        var all = deliveries.ToArray();
        all[0].Headers.ShouldBeNull();
        all[1].Headers.ShouldNotBeNull();
        all[1].Headers![TestMessageTransport.DeliveryAttemptHeader].ShouldBe("2");
        all[1].MessageId.ShouldBe(all[0].MessageId);
    }

    [Fact]
    public async Task Retry_result_waits_out_the_configured_backoff_before_redelivering()
    {
        var options = ZeroDelayRetryOptions();
        options.ConsumerRetry.InitialInterval = TimeSpan.FromMilliseconds(300);
        options.ConsumerRetry.MaxInterval = TimeSpan.FromMilliseconds(300);

        await using var transport = new TestMessageTransport(options);
        var deliveries = new ConcurrentQueue<DateTime>();

        await transport.StartConsumingAsync(
            [new TransportSubscription(typeof(object), "Test.Event")],
            (_, _) =>
            {
                deliveries.Enqueue(DateTime.UtcNow);
                return Task.FromResult(deliveries.Count == 1
                    ? MessageDispatchResult.Retry
                    : MessageDispatchResult.Acknowledge);
            });

        await transport.PublishAsync(Envelope());

        await TestWait.WaitForConditionAsync(() => deliveries.Count == 2);
        var times = deliveries.ToArray();
        (times[1] - times[0]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Scheduled_publish_delivers_after_the_enqueue_time_with_the_schedule_cleared()
    {
        await using var transport = new TestMessageTransport();
        var received = new ConcurrentQueue<TransportEnvelope>();

        await transport.StartConsumingAsync(
            [new TransportSubscription(typeof(object), "Test.Event")],
            (envelope, _) =>
            {
                received.Enqueue(envelope);
                return Task.FromResult(MessageDispatchResult.Acknowledge);
            });

        var sent = Envelope() with { ScheduledEnqueueTimeUtc = DateTimeOffset.UtcNow.AddMilliseconds(300) };
        await transport.PublishAsync(sent);

        await Task.Delay(100);
        received.ShouldBeEmpty();

        await TestWait.WaitForConditionAsync(() => received.Count == 1);
        received.TryDequeue(out var delivered).ShouldBeTrue();
        delivered!.MessageId.ShouldBe(sent.MessageId);
        delivered.ScheduledEnqueueTimeUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Scheduled_publish_with_a_past_time_delivers_immediately()
    {
        await using var transport = new TestMessageTransport();
        var received = new ConcurrentQueue<TransportEnvelope>();

        await transport.StartConsumingAsync(
            [new TransportSubscription(typeof(object), "Test.Event")],
            (envelope, _) =>
            {
                received.Enqueue(envelope);
                return Task.FromResult(MessageDispatchResult.Acknowledge);
            });

        await transport.PublishAsync(Envelope() with { ScheduledEnqueueTimeUtc = DateTimeOffset.UtcNow.AddSeconds(-5) });

        await TestWait.WaitForConditionAsync(() => received.Count == 1);
    }

    [Fact]
    public async Task PublishFailure_set_ThrowsInsteadOfPublishing_AndDoesNotRecordTheEnvelope()
    {
        await using var transport = new TestMessageTransport
        {
            PublishFailure = new InvalidOperationException("simulated broker outage"),
        };

        await Should.ThrowAsync<InvalidOperationException>(() => transport.PublishAsync(Envelope()));

        transport.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublishAsync_RecordsEveryEnvelope_EvenWithNoSubscriber()
    {
        await using var transport = new TestMessageTransport();

        await transport.PublishAsync(Envelope("Some.Unsubscribed.Type"));

        transport.Published.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DeadLettered_CapturesTheEnvelope_WhenTheCallbackReturnsDeadLetter()
    {
        await using var transport = new TestMessageTransport();

        await transport.StartConsumingAsync(
            [new TransportSubscription(typeof(object), "Test.Event")],
            (_, _) => Task.FromResult(MessageDispatchResult.DeadLetter));

        var sent = Envelope();
        await transport.PublishAsync(sent);

        await TestWait.WaitForConditionAsync(() => transport.DeadLettered.Count == 1);
        transport.DeadLettered.ShouldHaveSingleItem().MessageId.ShouldBe(sent.MessageId);
    }

    [Fact]
    public async Task PublishedEventsOf_And_DeadLetteredEventsOf_DeserializeMatchingEnvelopes()
    {
        await using var transport = new TestMessageTransport();

        await transport.StartConsumingAsync(
            [new TransportSubscription(typeof(TestOrderPlacedEvent), typeof(TestOrderPlacedEvent).FullName!)],
            (_, _) => Task.FromResult(MessageDispatchResult.DeadLetter));

        var @event = new TestOrderPlacedEvent { OrderId = 7, CustomerName = "Grace" };
        await transport.PublishAsync(EventEnvelope(@event));

        await TestWait.WaitForConditionAsync(() => transport.DeadLettered.Count == 1);

        transport.PublishedEventsOf<TestOrderPlacedEvent>().ShouldHaveSingleItem().OrderId.ShouldBe(7);
        transport.DeadLetteredEventsOf<TestOrderPlacedEvent>().ShouldHaveSingleItem().CustomerName.ShouldBe("Grace");
    }
}
