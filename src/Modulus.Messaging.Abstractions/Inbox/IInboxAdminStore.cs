namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Operator-facing administration surface for the inbox. Separated from
/// <see cref="IInboxStore"/> (runtime idempotency path) the same way
/// <see cref="IOutboxAdminStore"/> is split from <see cref="IOutboxStore"/>, so the hot path
/// stays focused while retention tooling has its own primitives.
/// </summary>
public interface IInboxAdminStore
{
    /// <summary>
    /// Counts inbox messages whose <see cref="InboxMessage.OccurredOnUtc"/> is before
    /// <paramref name="olderThanUtc"/> — the rows <see cref="PurgeOldAsync"/> would remove.
    /// </summary>
    Task<int> CountOldAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes at most <paramref name="batchSize"/> inbox messages (and their
    /// per-handler consumer rows) whose <see cref="InboxMessage.OccurredOnUtc"/> is before
    /// <paramref name="olderThanUtc"/>, oldest first, and returns the number of messages
    /// removed. Callers purge to completion by repeating until the return value is less than
    /// <paramref name="batchSize"/>.
    /// </summary>
    /// <remarks>
    /// Purged rows leave the deduplication window: a broker redelivery of a purged message
    /// re-executes its handlers. Only purge past your broker's maximum redelivery horizon
    /// (dead-letter replays included).
    /// </remarks>
    Task<int> PurgeOldAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken = default);
}
