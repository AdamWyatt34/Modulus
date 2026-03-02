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
    /// Retrieves a batch of unprocessed outbox messages.
    /// </summary>
    /// <param name="batchSize">The maximum number of messages to retrieve.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A read-only list of pending outbox messages.</returns>
    Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the specified outbox messages as processed.
    /// </summary>
    /// <param name="ids">The identifiers of the messages to mark as processed.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
