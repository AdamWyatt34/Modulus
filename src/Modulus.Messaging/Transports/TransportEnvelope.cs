namespace Modulus.Messaging.Transports;

/// <summary>
/// The transport-neutral wrapper for a message in flight. The body is the raw
/// System.Text.Json bytes of the event; all metadata travels alongside it and is
/// mapped to each broker's native message properties.
/// </summary>
/// <param name="MessageType">The stable type name (namespace-qualified, assembly-neutral) used for routing and deserialization.</param>
/// <param name="MessageId">The unique message identifier; equals <c>IIntegrationEvent.EventId</c> for events.</param>
/// <param name="CorrelationId">Optional correlation identifier for distributed tracing.</param>
/// <param name="OccurredOn">The UTC timestamp when the message was created.</param>
/// <param name="Body">The serialized message payload (System.Text.Json, default options).</param>
/// <param name="ContentType">The MIME type of <paramref name="Body"/>.</param>
public sealed record TransportEnvelope(
    string MessageType,
    Guid MessageId,
    string? CorrelationId,
    DateTime OccurredOn,
    ReadOnlyMemory<byte> Body,
    string ContentType = "application/json");
