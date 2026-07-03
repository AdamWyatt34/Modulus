using System.Text.Json;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Serialization;

public class MessageSerializerTests
{
    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsEvent()
    {
        var @event = new TestOrderCreatedEvent
        {
            OrderId = 7,
            CustomerName = "Round Trip",
            CorrelationId = "corr-1"
        };

        var bytes = MessageSerializer.Serialize(@event, typeof(TestOrderCreatedEvent));
        var result = MessageSerializer.Deserialize(bytes, typeof(TestOrderCreatedEvent));

        var deserialized = result.ShouldBeOfType<TestOrderCreatedEvent>();
        deserialized.OrderId.ShouldBe(7);
        deserialized.CustomerName.ShouldBe("Round Trip");
        deserialized.CorrelationId.ShouldBe("corr-1");
        deserialized.EventId.ShouldBe(@event.EventId);
    }

    [Fact]
    public void Deserialize_PayloadWrittenWithDefaultStjOptions_IsCompatible()
    {
        // EfOutboxStore serializes payloads with JsonSerializer default options;
        // the transport serializer must read those rows unchanged.
        var @event = new TestOrderCreatedEvent { OrderId = 42, CustomerName = "Outbox Row" };
        var outboxStylePayload = JsonSerializer.Serialize(@event, typeof(TestOrderCreatedEvent));

        var result = MessageSerializer.Deserialize(
            System.Text.Encoding.UTF8.GetBytes(outboxStylePayload),
            typeof(TestOrderCreatedEvent));

        var deserialized = result.ShouldBeOfType<TestOrderCreatedEvent>();
        deserialized.OrderId.ShouldBe(42);
        deserialized.CustomerName.ShouldBe("Outbox Row");
    }
}
