namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Represents an incoming integration event stored in the inbox for deduplication.
/// </summary>
public sealed class InboxMessage
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Content { get; init; }
    public required DateTime OccurredOnUtc { get; init; }
    public DateTime? ProcessedOnUtc { get; set; }
}
