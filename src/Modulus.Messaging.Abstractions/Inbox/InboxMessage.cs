namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Represents an incoming integration event stored in the inbox for deduplication.
/// </summary>
public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime OccurredOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
}
