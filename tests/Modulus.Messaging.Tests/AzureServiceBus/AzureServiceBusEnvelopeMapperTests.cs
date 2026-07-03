using System.Globalization;
using Azure.Messaging.ServiceBus;
using Modulus.Messaging.AzureServiceBus;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.AzureServiceBus;

public class AzureServiceBusEnvelopeMapperTests
{
    [Fact]
    public void ToServiceBusMessage_MapsAllMetadata()
    {
        var envelope = new TransportEnvelope(
            "My.Event",
            Guid.NewGuid(),
            "corr-9",
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            "{}"u8.ToArray());

        var message = AzureServiceBusEnvelopeMapper.ToServiceBusMessage(envelope);

        message.MessageId.ShouldBe(envelope.MessageId.ToString());
        message.CorrelationId.ShouldBe("corr-9");
        message.Subject.ShouldBe("My.Event");
        message.ContentType.ShouldBe("application/json");
        message.ApplicationProperties[AzureServiceBusEnvelopeMapper.OccurredOnProperty]
            .ShouldBe(envelope.OccurredOn.ToString("O", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ToEnvelope_ReceivedMessage_RoundTripsMetadata()
    {
        var messageId = Guid.NewGuid();
        var occurredOn = new DateTime(2026, 7, 1, 8, 30, 0, DateTimeKind.Utc);

        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{\"a\":1}"),
            messageId: messageId.ToString(),
            correlationId: "corr-42",
            subject: "My.Event",
            contentType: "application/json",
            properties: new Dictionary<string, object>
            {
                [AzureServiceBusEnvelopeMapper.OccurredOnProperty] =
                    occurredOn.ToString("O", CultureInfo.InvariantCulture),
            });

        var envelope = AzureServiceBusEnvelopeMapper.ToEnvelope(received);

        envelope.MessageType.ShouldBe("My.Event");
        envelope.MessageId.ShouldBe(messageId);
        envelope.CorrelationId.ShouldBe("corr-42");
        envelope.OccurredOn.ShouldBe(occurredOn);
        System.Text.Encoding.UTF8.GetString(envelope.Body.Span).ShouldBe("{\"a\":1}");
    }

    [Fact]
    public void ToEnvelope_MissingMetadata_UsesSafeDefaults()
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"));

        var envelope = AzureServiceBusEnvelopeMapper.ToEnvelope(received);

        envelope.MessageType.ShouldBe(string.Empty);
        envelope.MessageId.ShouldBe(Guid.Empty);
        envelope.CorrelationId.ShouldBeNull();
        envelope.ContentType.ShouldBe("application/json");
    }
}
