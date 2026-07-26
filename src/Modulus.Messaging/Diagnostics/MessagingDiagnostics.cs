namespace Modulus.Messaging.Diagnostics;

/// <summary>
/// The telemetry names Modulus messaging emits under, for OpenTelemetry configuration:
/// <c>AddSource(MessagingDiagnostics.ActivitySourceName)</c> for publish/consume spans,
/// <c>AddSource(MessagingDiagnostics.OutboxActivitySourceName)</c> for outbox dispatch spans,
/// and <c>AddMeter("Modulus.Messaging")</c> for metrics.
/// </summary>
public static class MessagingDiagnostics
{
    /// <summary>Activity source for message-bus publishes and consumer-side processing.</summary>
    public const string ActivitySourceName = "Modulus.Messaging";

    /// <summary>Activity source for outbox dispatch (mirrors <c>OutboxDispatcher</c>'s source).</summary>
    public const string OutboxActivitySourceName = "Modulus.Messaging.Outbox";

    /// <summary>The envelope header carrying the W3C <c>traceparent</c> value.</summary>
    public const string TraceParentHeader = "traceparent";

    /// <summary>The envelope header carrying the W3C <c>tracestate</c> value.</summary>
    public const string TraceStateHeader = "tracestate";
}
