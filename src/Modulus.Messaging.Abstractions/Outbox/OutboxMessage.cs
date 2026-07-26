namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Represents a message stored in the transactional outbox.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Gets the unique identifier of the outbox message.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the assembly-qualified type name of the event.</summary>
    public required string EventType { get; init; }

    /// <summary>Gets the JSON-serialized event payload.</summary>
    public required string Payload { get; init; }

    /// <summary>Gets the UTC timestamp when the message was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Gets or sets the UTC timestamp when the message was processed, or <see langword="null"/> if pending.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Gets or sets the number of publish attempts that have failed for this message.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets the error message from the most recent failed publish attempt.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which this message becomes eligible for another dispatch
    /// attempt, or <see langword="null"/> if it has never failed or is immediately eligible.
    /// Set by <see cref="IOutboxStore.MarkAsFailed"/> from the caller's configured retry
    /// backoff, so a persistently failing row (including a poison row with an unrecognized
    /// or undeserializable payload) does not busy-loop the dispatcher every pass until it
    /// dead-letters.
    /// </summary>
    public DateTime? NextAttemptOnUtc { get; set; }

    /// <summary>
    /// Gets the earliest UTC time this message may be dispatched, or <see langword="null"/>
    /// for immediate eligibility. Set by the scheduled
    /// <see cref="IOutboxStore.Save(IIntegrationEvent, DateTimeOffset, CancellationToken)"/>
    /// overload; distinct from <see cref="NextAttemptOnUtc"/>, which is strictly the retry
    /// backoff written after a failed attempt (and overwritten on every failure). Rows
    /// scheduled for the future are excluded from both dispatch and the backlog count — a
    /// message scheduled a week out is not outstanding work for the health check.
    /// </summary>
    public DateTime? ScheduledOnUtc { get; init; }

    /// <summary>
    /// Gets the W3C <c>traceparent</c> of the operation that saved this message, or
    /// <see langword="null"/> when no trace was active at save time (or the row predates
    /// trace capture). The outbox dispatcher links its publish span to this context so the
    /// originating request stays reachable from the consumer-side trace.
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>Gets the W3C <c>tracestate</c> accompanying <see cref="TraceParent"/>, if any.</summary>
    public string? TraceState { get; init; }
}
