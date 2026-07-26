namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Operator-facing administration surface for the transactional outbox.
/// Separated from <see cref="IOutboxStore"/> (runtime publish path) so that the runtime
/// store can stay tightly focused on the polling hot path while admin tooling has its own
/// read/mutate primitives.
/// </summary>
public interface IOutboxAdminStore
{
    /// <summary>
    /// Returns dead-lettered messages whose attempt count meets or exceeds <paramref name="maxAttempts"/>
    /// and that have not been processed.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> GetFailedAsync(
        int maxAttempts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the attempt counter and last-error for a single message so the outbox processor
    /// will retry it on the next poll. Returns <see langword="false"/> if the message is unknown.
    /// </summary>
    Task<bool> RetryAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes a message from the outbox. Returns <see langword="false"/> if the
    /// message is unknown.
    /// </summary>
    Task<bool> PurgeAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts processed messages whose <see cref="OutboxMessage.ProcessedAt"/> is before
    /// <paramref name="olderThanUtc"/> — the rows <see cref="PurgeProcessedAsync"/> would remove.
    /// </summary>
    /// <remarks>
    /// A default interface implementation throws <see cref="NotSupportedException"/> so custom
    /// stores written against earlier versions keep compiling; override to support retention.
    /// </remarks>
    Task<int> CountProcessedAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not implement {nameof(CountProcessedAsync)}. " +
            "Override it to support outbox retention.");

    /// <summary>
    /// Permanently removes at most <paramref name="batchSize"/> processed messages whose
    /// <see cref="OutboxMessage.ProcessedAt"/> is before <paramref name="olderThanUtc"/>,
    /// oldest first, and returns the number removed. Unprocessed rows — pending, backing off,
    /// or dead-lettered — are never touched: they represent undelivered work an operator may
    /// still retry. Callers purge to completion by repeating until the return value is less
    /// than <paramref name="batchSize"/>.
    /// </summary>
    /// <remarks>
    /// A default interface implementation throws <see cref="NotSupportedException"/> so custom
    /// stores written against earlier versions keep compiling; override to support retention.
    /// </remarks>
    Task<int> PurgeProcessedAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not implement {nameof(PurgeProcessedAsync)}. " +
            "Override it to support outbox retention.");
}
