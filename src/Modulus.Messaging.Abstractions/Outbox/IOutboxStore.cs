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
    /// <paramref name="maxAttempts"/>. Dead-lettered rows (Attempts &gt;= maxAttempts) are
    /// excluded so they do not starve newer rows out of the polling batch.
    /// </summary>
    /// <param name="batchSize">The maximum number of messages to retrieve.</param>
    /// <param name="maxAttempts">Messages whose <see cref="OutboxMessage.Attempts"/> is at or above this value are excluded.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A read-only list of pending outbox messages eligible for publishing.</returns>
    Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, int maxAttempts, CancellationToken cancellationToken = default);

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
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsFailed(Guid messageId, string error, CancellationToken cancellationToken = default);
}
