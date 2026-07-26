using System.Globalization;
using Azure.Messaging.ServiceBus;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.AzureServiceBus;

/// <summary>
/// Maps between <see cref="TransportEnvelope"/> and native Service Bus message properties.
/// Metadata rides in standard fields (MessageId, CorrelationId, Subject, ContentType) plus a
/// <c>modulus-occurred-on</c> application property; envelope headers — including the W3C
/// <c>traceparent</c>/<c>tracestate</c> trace context — map to application properties as-is;
/// the body is the bare event JSON.
/// </summary>
internal static class AzureServiceBusEnvelopeMapper
{
    internal const string OccurredOnProperty = "modulus-occurred-on";

    /// <summary>
    /// The property the Azure SDK's own instrumentation stamps a W3C traceparent into.
    /// Honored on receive as a fallback so messages from non-Modulus publishers still join
    /// their producer's trace.
    /// </summary>
    internal const string DiagnosticIdProperty = "Diagnostic-Id";

    internal const string TraceParentHeader = "traceparent";

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

        if (envelope.Headers is not null)
        {
            foreach (var (key, value) in envelope.Headers)
                message.ApplicationProperties[key] = value;
        }

        // Native scheduling: the broker holds the message until the enqueue time.
        if (envelope.ScheduledEnqueueTimeUtc is { } enqueueAt)
            message.ScheduledEnqueueTime = enqueueAt;

        return message;
    }

    public static TransportEnvelope ToEnvelope(ServiceBusReceivedMessage message)
        => new(
            message.Subject ?? string.Empty,
            Guid.TryParse(message.MessageId, out var messageId) ? messageId : Guid.Empty,
            string.IsNullOrEmpty(message.CorrelationId) ? null : message.CorrelationId,
            ReadOccurredOn(message),
            message.Body.ToMemory(),
            message.ContentType ?? "application/json")
        {
            Headers = ReadEnvelopeHeaders(message),
        };

    private static IReadOnlyDictionary<string, string>? ReadEnvelopeHeaders(ServiceBusReceivedMessage message)
    {
        Dictionary<string, string>? result = null;

        foreach (var (key, raw) in message.ApplicationProperties)
        {
            if (string.Equals(key, OccurredOnProperty, StringComparison.Ordinal) || raw is not string text)
                continue;

            result ??= new Dictionary<string, string>(StringComparer.Ordinal);
            result[key] = text;
        }

        // Interop: a non-Modulus publisher instrumented by the Azure SDK carries its trace
        // context in Diagnostic-Id; surface it as traceparent when none was set explicitly.
        if (result is not null
            && !result.ContainsKey(TraceParentHeader)
            && result.TryGetValue(DiagnosticIdProperty, out var diagnosticId))
        {
            result[TraceParentHeader] = diagnosticId;
        }

        return result;
    }

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
