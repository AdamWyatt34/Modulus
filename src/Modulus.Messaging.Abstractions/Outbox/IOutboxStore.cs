namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Abstraction for the transactional outbox pattern.
/// Stores integration events to be dispatched reliably after the transaction commits.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Saves an integration event to the outbox within the current transaction.
    /// </summary>
    /// <param name="event">The integration event to store.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a batch of unprocessed outbox messages whose attempt count is below
    /// <paramref name="maxAttempts"/> and whose <see cref="OutboxMessage.NextAttemptOnUtc"/> is
    /// unset or has elapsed. Dead-lettered rows (Attempts &gt;= maxAttempts) and rows still
    /// serving out a retry backoff are excluded so they do not starve newer rows out of the
    /// polling batch or cause the dispatcher to busy-loop retrying them every pass.
    /// </summary>
    /// <param name="batchSize">The maximum number of messages to retrieve.</param>
    /// <param name="maxAttempts">Messages whose <see cref="OutboxMessage.Attempts"/> is at or above this value are excluded.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A read-only list of pending outbox messages eligible for publishing.</returns>
    Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, int maxAttempts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts unprocessed outbox messages whose attempt count is below <paramref name="maxAttempts"/>.
    /// Used by the backlog-depth health check. Unlike <see cref="GetPending"/>, this intentionally
    /// includes rows currently serving out a retry backoff (<see cref="OutboxMessage.NextAttemptOnUtc"/>
    /// in the future) — backlog depth should reflect true outstanding work, including messages a
    /// broker outage has pushed into backoff, not just what is immediately fetchable.
    /// </summary>
    /// <param name="maxAttempts">Messages whose <see cref="OutboxMessage.Attempts"/> is at or above this value are excluded.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of pending outbox messages not yet dead-lettered.</returns>
    Task<int> CountPending(int maxAttempts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the specified outbox messages as processed.
    /// </summary>
    /// <param name="ids">The identifiers of the messages to mark as processed.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the attempt counter for an outbox message and records the failure message.
    /// </summary>
    /// <param name="messageId">The identifier of the failed message.</param>
    /// <param name="error">A human-readable error message.</param>
    /// <param name="nextAttemptOnUtc">
    /// The UTC time at which <see cref="GetPending"/> may return this row again, or
    /// <see langword="null"/> to make it immediately eligible again. Callers should pass the
    /// backoff computed from the configured retry policy so a persistently failing row —
    /// including a poison row that can never succeed — does not busy-loop the dispatcher.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsFailed(Guid messageId, string error, DateTime? nextAttemptOnUtc, CancellationToken cancellationToken = default);
}
