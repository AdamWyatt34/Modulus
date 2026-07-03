using System.Text.Json;

namespace Modulus.Messaging.Serialization;

/// <summary>
/// Serializes message bodies with System.Text.Json default options. Default options are
/// load-bearing: <c>EfOutboxStore</c> persists outbox payloads the same way, so rows written
/// before an upgrade must stay deserializable here.
/// </summary>
internal static class MessageSerializer
{
    public static byte[] Serialize(object message, Type messageType)
        => JsonSerializer.SerializeToUtf8Bytes(message, messageType);

    public static object? Deserialize(ReadOnlyMemory<byte> body, Type messageType)
        => JsonSerializer.Deserialize(body.Span, messageType);
}
