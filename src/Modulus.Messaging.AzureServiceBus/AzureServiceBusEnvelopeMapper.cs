using System.Globalization;
using Azure.Messaging.ServiceBus;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.AzureServiceBus;

/// <summary>
/// Maps between <see cref="TransportEnvelope"/> and native Service Bus message properties.
/// Metadata rides in standard fields (MessageId, CorrelationId, Subject, ContentType) plus a
/// <c>modulus-occurred-on</c> application property; the body is the bare event JSON.
/// </summary>
internal static class AzureServiceBusEnvelopeMapper
{
    internal const string OccurredOnProperty = "modulus-occurred-on";

    public static ServiceBusMessage ToServiceBusMessage(TransportEnvelope envelope)
    {
        var message = new ServiceBusMessage(BinaryData.FromBytes(envelope.Body))
        {
            MessageId = envelope.MessageId.ToString(),
            CorrelationId = envelope.CorrelationId,
            Subject = envelope.MessageType,
            ContentType = envelope.ContentType,
        };

        message.ApplicationProperties[OccurredOnProperty] =
            envelope.OccurredOn.ToString("O", CultureInfo.InvariantCulture);

        return message;
    }

    public static TransportEnvelope ToEnvelope(ServiceBusReceivedMessage message)
        => new(
            message.Subject ?? string.Empty,
            Guid.TryParse(message.MessageId, out var messageId) ? messageId : Guid.Empty,
            string.IsNullOrEmpty(message.CorrelationId) ? null : message.CorrelationId,
            ReadOccurredOn(message),
            message.Body.ToMemory(),
            message.ContentType ?? "application/json");

    private static DateTime ReadOccurredOn(ServiceBusReceivedMessage message)
    {
        if (message.ApplicationProperties.TryGetValue(OccurredOnProperty, out var raw)
            && raw is string text
            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurredOn))
        {
            return occurredOn;
        }

        return DateTime.UtcNow;
    }
}
