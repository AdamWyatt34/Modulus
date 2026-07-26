using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Messaging.InMemory;
using Modulus.Messaging.Tests.Fixtures;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.InMemory;

public class InMemoryTransportRedeliveryTests
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

    private static TransportEnvelope Envelope(string type = "Test.Event") => new(
        type,
        Guid.NewGuid(),
        null,
        DateTime.UtcNow,
        "{}"u8.ToArray());

    [Fact]
    public async Task Retry_result_redelivers_a_copy_with_the_incremented_attempt_header()
    {
        await using var transport = new InMemoryTransport(NullLogger<InMemoryTransport>.Instance, ZeroDelayRetryOptions());
        var deliveries = new ConcurrentQueue<TransportEnvelope>();

        await transport.StartConsumingAsync(
            [new TransportSubscription(typeof(object), "Test.Event")],
            (envelope, _) =>
            {
                deliveries.Enqueue(envelope);
                // Fail the first delivery, succeed the redelivered copy.
                return Task.FromResult(deliveries.Count == 1
                    ? MessageDispatchResult.Retry
                    : MessageDispatchResult.Acknowledge);
            });

        await transport.PublishAsync(Envelope());

        await TestWait.WaitForConditionAsync(() => deliveries.Count == 2);
        var all = deliveries.ToArray();
        all[0].Headers.ShouldBeNull();
        all[1].Headers.ShouldNotBeNull();
        all[1].Headers!["modulus-delivery-attempt"].ShouldBe("2");
        all[1].MessageId.ShouldBe(all[0].MessageId);
    }

    [Fact]
    public async Task Retry_result_waits_out_the_configured_backoff_before_redelivering()
    {
        var options = ZeroDelayRetryOptions();
        options.ConsumerRetry.InitialInterval = TimeSpan.FromMilliseconds(300);
        options.ConsumerRetry.MaxInterval = TimeSpan.FromMilliseconds(300);

        await using var transport = new InMemoryTransport(NullLogger<InMemoryTransport>.Instance, options);
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
        // Generous lower bound: proves an actual delay happened without flaking on timers.
        (times[1] - times[0]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Scheduled_publish_delivers_after_the_enqueue_time_with_the_schedule_cleared()
    {
        await using var transport = new InMemoryTransport(NullLogger<InMemoryTransport>.Instance);
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
        await using var transport = new InMemoryTransport(NullLogger<InMemoryTransport>.Instance);
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
}
