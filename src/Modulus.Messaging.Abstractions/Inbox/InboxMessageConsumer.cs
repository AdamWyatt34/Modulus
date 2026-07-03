namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Tracks per-handler consumption of an inbox message. A row is inserted as a
/// <em>reservation</em> before the handler runs (claiming the (message, handler) pair
/// against concurrent duplicate deliveries) and stamped with <see cref="ProcessedOnUtc"/>
/// once the handler succeeds. A reservation whose <see cref="ReservedOnUtc"/> is older
/// than the configured timeout with no <see cref="ProcessedOnUtc"/> is considered
/// abandoned (e.g. the owning process crashed) and may be taken over by a later delivery.
/// </summary>
public sealed class InboxMessageConsumer
{
    public required Guid InboxMessageId { get; init; }
    public required string Name { get; init; }
    public DateTime ReservedOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
}
