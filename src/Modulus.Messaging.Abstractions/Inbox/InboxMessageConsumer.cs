namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Tracks which handlers have already processed a given inbox message,
/// enabling idempotent integration event handling.
/// </summary>
public sealed class InboxMessageConsumer
{
    public Guid InboxMessageId { get; set; }
    public string Name { get; set; } = string.Empty;
}
