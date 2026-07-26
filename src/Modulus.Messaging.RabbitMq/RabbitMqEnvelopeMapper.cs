using System.Globalization;
using Modulus.Messaging.Transports;
using RabbitMQ.Client;

namespace Modulus.Messaging.RabbitMq;

/// <summary>
/// Maps between <see cref="TransportEnvelope"/> and native AMQP message properties.
/// Metadata rides in standard fields (MessageId, CorrelationId, Type, ContentType) plus a
/// <c>modulus-occurred-on</c> header; envelope headers — including the W3C
/// <c>traceparent</c>/<c>tracestate</c> trace context — map to AMQP headers as-is; the body
/// is the bare event JSON.
/// </summary>
internal static class RabbitMqEnvelopeMapper
{
    internal const string OccurredOnHeader = "modulus-occurred-on";

    public static BasicProperties ToBasicProperties(TransportEnvelope envelope)
    {
        var headers = new Dictionary<string, object?>
        {
            [OccurredOnHeader] = envelope.OccurredOn.ToString("O", CultureInfo.InvariantCulture),
        };

        if (envelope.Headers is not null)
        {
            foreach (var (key, value) in envelope.Headers)
                headers[key] = value;
        }

        return new BasicProperties
        {
            Persistent = true,
            MessageId = envelope.MessageId.ToString(),
            CorrelationId = envelope.CorrelationId,
            Type = envelope.MessageType,
            ContentType = envelope.ContentType,
            Headers = headers,
        };
    }

    public static TransportEnvelope ToEnvelope(IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body)
        => new(
            properties.Type ?? string.Empty,
            Guid.TryParse(properties.MessageId, out var messageId) ? messageId : Guid.Empty,
            properties.CorrelationId,
            ReadOccurredOn(properties),
            body,
            properties.ContentType ?? "application/json")
        {
            Headers = ReadEnvelopeHeaders(properties),
        };

    private static IReadOnlyDictionary<string, string>? ReadEnvelopeHeaders(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is not { Count: > 0 } headers)
            return null;

        Dictionary<string, string>? result = null;

        foreach (var (key, raw) in headers)
        {
            if (string.Equals(key, OccurredOnHeader, StringComparison.Ordinal))
                continue;

            if (DecodeHeaderValue(raw) is not { } text)
                continue;

            result ??= new Dictionary<string, string>(StringComparer.Ordinal);
            result[key] = text;
        }

        return result;
    }

    // RabbitMQ delivers string headers as byte arrays.
    private static string? DecodeHeaderValue(object? raw) => raw switch
    {
        byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
        string s => s,
        _ => null,
    };

    private static DateTime ReadOccurredOn(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is { } headers
            && headers.TryGetValue(OccurredOnHeader, out var raw)
            && DecodeHeaderValue(raw) is { } text
            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurredOn))
        {
            return occurredOn;
        }

        return DateTime.UtcNow;
    }
}
