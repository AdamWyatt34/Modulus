using Modulus.Messaging.RabbitMq;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.RabbitMq;

public class RabbitMqEnvelopeMapperTests
{
    [Fact]
    public void ToBasicProperties_MapsAllMetadata()
    {
        var envelope = new TransportEnvelope(
            "My.Event",
            Guid.NewGuid(),
            "corr-9",
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            "{}"u8.ToArray());

        var properties = RabbitMqEnvelopeMapper.ToBasicProperties(envelope);

        properties.Persistent.ShouldBeTrue();
        properties.MessageId.ShouldBe(envelope.MessageId.ToString());
        properties.CorrelationId.ShouldBe("corr-9");
        properties.Type.ShouldBe("My.Event");
        properties.ContentType.ShouldBe("application/json");
        properties.Headers!.ShouldContainKey(RabbitMqEnvelopeMapper.OccurredOnHeader);
    }

    [Fact]
    public void RoundTrip_PreservesEnvelopeMetadata()
    {
        var original = new TransportEnvelope(
            "My.Event",
            Guid.NewGuid(),
            "corr-42",
            new DateTime(2026, 7, 1, 8, 30, 0, DateTimeKind.Utc),
            "{\"a\":1}"u8.ToArray());

        var properties = RabbitMqEnvelopeMapper.ToBasicProperties(original);

        // Broker delivers string headers as UTF-8 byte arrays; simulate that.
        var occurredOnText = (string)properties.Headers![RabbitMqEnvelopeMapper.OccurredOnHeader]!;
        properties.Headers[RabbitMqEnvelopeMapper.OccurredOnHeader] =
            System.Text.Encoding.UTF8.GetBytes(occurredOnText);

        var roundTripped = RabbitMqEnvelopeMapper.ToEnvelope(properties, original.Body);

        roundTripped.MessageType.ShouldBe(original.MessageType);
        roundTripped.MessageId.ShouldBe(original.MessageId);
        roundTripped.CorrelationId.ShouldBe(original.CorrelationId);
        roundTripped.OccurredOn.ShouldBe(original.OccurredOn);
        roundTripped.Body.ToArray().ShouldBe(original.Body.ToArray());
    }

    [Fact]
    public void ToEnvelope_MissingMetadata_UsesSafeDefaults()
    {
        var properties = new RabbitMQ.Client.BasicProperties();

        var envelope = RabbitMqEnvelopeMapper.ToEnvelope(properties, "{}"u8.ToArray());

        envelope.MessageType.ShouldBe(string.Empty);
        envelope.MessageId.ShouldBe(Guid.Empty);
        envelope.CorrelationId.ShouldBeNull();
        envelope.ContentType.ShouldBe("application/json");
    }
}
