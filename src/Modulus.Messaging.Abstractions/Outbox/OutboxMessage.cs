namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Represents a message stored in the transactional outbox.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Gets or sets the unique identifier of the outbox message.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets or sets the assembly-qualified type name of the event.</summary>
    public required string EventType { get; init; }

    /// <summary>Gets or sets the JSON-serialized event payload.</summary>
    public required string Payload { get; init; }

    /// <summary>Gets or sets the UTC timestamp when the message was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Gets or sets the UTC timestamp when the message was processed, or <see langword="null"/> if pending.</summary>
    public DateTime? ProcessedAt { get; set; }
}
