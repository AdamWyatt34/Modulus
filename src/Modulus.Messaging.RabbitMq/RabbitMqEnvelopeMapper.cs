using System.Globalization;
using Modulus.Messaging.Transports;
using RabbitMQ.Client;

namespace Modulus.Messaging.RabbitMq;

/// <summary>
/// Maps between <see cref="TransportEnvelope"/> and native AMQP message properties.
/// Metadata rides in standard fields (MessageId, CorrelationId, Type, ContentType) plus a
/// <c>modulus-occurred-on</c> header; the body is the bare event JSON.
/// </summary>
internal static class RabbitMqEnvelopeMapper
{
    internal const string OccurredOnHeader = "modulus-occurred-on";

    public static BasicProperties ToBasicProperties(TransportEnvelope envelope)
        => new()
        {
            Persistent = true,
            MessageId = envelope.MessageId.ToString(),
            CorrelationId = envelope.CorrelationId,
            Type = envelope.MessageType,
            ContentType = envelope.ContentType,
            Headers = new Dictionary<string, object?>
            {
                [OccurredOnHeader] = envelope.OccurredOn.ToString("O", CultureInfo.InvariantCulture),
            },
        };

    public static TransportEnvelope ToEnvelope(IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body)
        => new(
            properties.Type ?? string.Empty,
            Guid.TryParse(properties.MessageId, out var messageId) ? messageId : Guid.Empty,
            properties.CorrelationId,
            ReadOccurredOn(properties),
            body,
            properties.ContentType ?? "application/json");

    private static DateTime ReadOccurredOn(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is { } headers
            && headers.TryGetValue(OccurredOnHeader, out var raw))
        {
            // RabbitMQ delivers string headers as byte arrays.
            var text = raw switch
            {
                byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
                string s => s,
                _ => null,
            };

            if (text is not null
                && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurredOn))
            {
                return occurredOn;
            }
        }

        return DateTime.UtcNow;
    }
}
