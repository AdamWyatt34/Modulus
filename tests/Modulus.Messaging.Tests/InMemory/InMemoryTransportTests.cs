using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Messaging.InMemory;
using Modulus.Messaging.Tests.Fixtures;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.InMemory;

public class InMemoryTransportTests
{
    private static TransportEnvelope Envelope(string type = "Test.Event") => new(
        type,
        Guid.NewGuid(),
        null,
        DateTime.UtcNow,
        "{}"u8.ToArray());

    [Fact]
    public async Task Publish_WithSubscription_DeliversToCallback()
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

        var sent = Envelope();
        await transport.PublishAsync(sent);

        await TestWait.WaitForConditionAsync(() => received.Count == 1);
        received.TryDequeue(out var actual).ShouldBeTrue();
        actual!.MessageId.ShouldBe(sent.MessageId);
    }

    [Fact]
    public async Task Publish_NoSubscription_DropsWithoutError()
    {
        await using var transport = new InMemoryTransport(NullLogger<InMemoryTransport>.Instance);

        await Should.NotThrowAsync(() => transport.PublishAsync(Envelope("Nobody.Listens")));
    }

    [Fact]
    public async Task Publish_MultipleTypes_RoutesByMessageType()
    {
        await using var transport = new InMemoryTransport(NullLogger<InMemoryTransport>.Instance);
        var received = new ConcurrentQueue<string>();

        await transport.StartConsumingAsync(
            [
                new TransportSubscription(typeof(object), "Type.A"),
                new TransportSubscription(typeof(object), "Type.B"),
            ],
            (envelope, _) =>
            {
                received.Enqueue(envelope.MessageType);
                return Task.FromResult(MessageDispatchResult.Acknowledge);
            });

        await transport.PublishAsync(Envelope("Type.A"));
        await transport.PublishAsync(Envelope("Type.B"));
        await transport.PublishAsync(Envelope("Type.A"));

        await TestWait.WaitForConditionAsync(() => received.Count == 3);
        received.Count(t => t == "Type.A").ShouldBe(2);
        received.Count(t => t == "Type.B").ShouldBe(1);
    }

    [Fact]
    public async Task StopConsuming_BufferedMessages_AreDrainedBeforeStop()
    {
        var transport = new InMemoryTransport(NullLogger<InMemoryTransport>.Instance);
        var processed = 0;

        await transport.StartConsumingAsync(
            [new TransportSubscription(typeof(object), "Test.Event")],
            async (_, _) =>
            {
                await Task.Delay(10);
                Interlocked.Increment(ref processed);
                return MessageDispatchResult.Acknowledge;
            });

        for (var i = 0; i < 5; i++)
            await transport.PublishAsync(Envelope());

        await transport.StopConsumingAsync();

        processed.ShouldBe(5);
    }

    [Fact]
    public async Task DeadLetterResult_IsLoggedAndDropped_ProcessingContinues()
    {
        await using var transport = new InMemoryTransport(NullLogger<InMemoryTransport>.Instance);
        var calls = 0;

        await transport.StartConsumingAsync(
            [new TransportSubscription(typeof(object), "Test.Event")],
            (_, _) =>
            {
                var call = Interlocked.Increment(ref calls);
                return Task.FromResult(call == 1
                    ? MessageDispatchResult.DeadLetter
                    : MessageDispatchResult.Acknowledge);
            });

        await transport.PublishAsync(Envelope());
        await transport.PublishAsync(Envelope());

        await TestWait.WaitForConditionAsync(() => calls == 2);
    }
}
