using Modulus.Messaging.Serialization;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

public class MessageBusScheduledPublishTests
{
    private static (TransportMessageBus Bus, FakeMessageTransport Transport) BuildBus()
    {
        var transport = new FakeMessageTransport();
        var bus = new TransportMessageBus(
            transport,
            new MessageTypeRegistry([typeof(TestOrderCreatedEvent).Assembly]));
        return (bus, transport);
    }

    [Fact]
    public async Task PublishScheduled_stamps_the_enqueue_time_on_the_envelope()
    {
        var (bus, transport) = BuildBus();
        var enqueueAt = DateTimeOffset.UtcNow.AddMinutes(10);

        await bus.PublishScheduled(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "T" }, enqueueAt);

        var envelope = transport.Published.ShouldHaveSingleItem();
        envelope.ScheduledEnqueueTimeUtc.ShouldBe(enqueueAt);
    }

    [Fact]
    public async Task PublishScheduled_with_a_past_time_publishes_immediately()
    {
        var (bus, transport) = BuildBus();

        await bus.PublishScheduled(
            new TestOrderCreatedEvent { OrderId = 2, CustomerName = "T" },
            DateTimeOffset.UtcNow.AddSeconds(-30));

        var envelope = transport.Published.ShouldHaveSingleItem();
        envelope.ScheduledEnqueueTimeUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Publish_never_schedules()
    {
        var (bus, transport) = BuildBus();

        await bus.Publish(new TestOrderCreatedEvent { OrderId = 3, CustomerName = "T" });

        transport.Published.ShouldHaveSingleItem().ScheduledEnqueueTimeUtc.ShouldBeNull();
    }
}
