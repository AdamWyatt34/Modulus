namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Tracks which handlers have already processed a given inbox message,
/// enabling idempotent integration event handling.
/// </summary>
public sealed class InboxMessageConsumer
{
    public required Guid InboxMessageId { get; init; }
    public required string Name { get; init; }
}
