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
    /// Saves an integration event that the outbox holds until <paramref name="enqueueAtUtc"/>
    /// before dispatching — durable scheduled publishing with the same transactional
    /// guarantees as <see cref="Save(IIntegrationEvent, CancellationToken)"/>. Precision is
    /// bounded by the outbox poll interval once the row is due. A time at or before now
    /// dispatches on the next pass.
    /// </summary>
    /// <remarks>
    /// A default interface implementation throws <see cref="NotSupportedException"/> so custom
    /// stores written against earlier versions keep compiling; the shipped store overrides it.
    /// </remarks>
    Task Save(IIntegrationEvent @event, DateTimeOffset enqueueAtUtc, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not implement scheduled {nameof(Save)}. " +
            "Override it to support scheduled publishing through the outbox.");

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> unprocessed outbox messages for
    /// <paramref name="ownerId"/>, so that multiple dispatcher instances polling the same table
    /// (scaled-out replicas) never publish the same row at the same time. A message is eligible
    /// when it is unprocessed, its attempt count is below <paramref name="maxAttempts"/>, its
    /// <see cref="OutboxMessage.NextAttemptOnUtc"/> retry backoff and
    /// <see cref="OutboxMessage.ScheduledOnUtc"/> schedule have both elapsed, and it is either
    /// unclaimed or its <see cref="OutboxMessage.ClaimedUntil"/> lease has expired. Dead-lettered
    /// rows and rows still serving out a retry backoff are excluded so they do not starve newer
    /// rows out of the polling batch or cause the dispatcher to busy-loop retrying them every
    /// pass.
    /// </summary>
    /// <remarks>
    /// The claim is a lease, not a permanent assignment: a crashed or hung owner's rows become
    /// claimable again — by any owner, including itself on a later pass — once
    /// <paramref name="lease"/> elapses, with no operator intervention required. This is
    /// deliberately weaker than <c>SELECT ... FOR UPDATE SKIP LOCKED</c>: it is a portable,
    /// set-based optimistic claim (implemented with <c>ExecuteUpdateAsync</c>) rather than a
    /// provider-specific row lock, so the store stays usable against any relational EF Core
    /// provider.
    /// </remarks>
    /// <param name="ownerId">
    /// A stable identifier for the calling dispatcher instance (e.g. machine name plus a
    /// per-process GUID). Only this owner's own subsequent <see cref="MarkAsProcessed"/> and
    /// <see cref="MarkAsFailed"/> calls can act on the rows this call returns, for as long as the
    /// lease holds.
    /// </param>
    /// <param name="lease">
    /// How long the claim on each returned row is held before it becomes reclaimable by anyone.
    /// Must comfortably exceed the time a single dispatch pass takes, or a slow pass can lose its
    /// own claim before it finishes and race a concurrent claimant over the same rows.
    /// </param>
    /// <param name="batchSize">The maximum number of messages to claim.</param>
    /// <param name="maxAttempts">Messages whose <see cref="OutboxMessage.Attempts"/> is at or above this value are excluded.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A read-only list of outbox messages claimed for <paramref name="ownerId"/>, oldest first.</returns>
    Task<IReadOnlyList<OutboxMessage>> ClaimPending(
        string ownerId,
        TimeSpan lease,
        int batchSize,
        int maxAttempts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts unprocessed outbox messages whose attempt count is below <paramref name="maxAttempts"/>.
    /// Used by the backlog-depth health check. Unlike <see cref="ClaimPending"/>, this intentionally
    /// includes rows currently serving out a retry backoff (<see cref="OutboxMessage.NextAttemptOnUtc"/>
    /// in the future) and rows another instance currently holds a claim on — backlog depth should
    /// reflect true outstanding work, including messages a broker outage has pushed into backoff
    /// or that a peer instance has claimed but not yet published, not just what is immediately
    /// claimable by the caller.
    /// </summary>
    /// <param name="maxAttempts">Messages whose <see cref="OutboxMessage.Attempts"/> is at or above this value are excluded.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of pending outbox messages not yet dead-lettered.</returns>
    Task<int> CountPending(int maxAttempts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the specified outbox messages as processed, but only for rows <paramref name="ownerId"/>
    /// still holds the claim on. A row whose claim was taken over by another owner — because this
    /// owner's lease expired before it called this method — is silently left alone: the row is
    /// someone else's responsibility now, and stamping it processed here would be a lie about who
    /// actually published it (and could hide a duplicate publish from ever being noticed).
    /// </summary>
    /// <param name="ownerId">The owner id passed to the <see cref="ClaimPending"/> call that returned these rows.</param>
    /// <param name="ids">The identifiers of the messages to mark as processed.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsProcessed(string ownerId, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the attempt counter for an outbox message, records the failure message, and
    /// releases its claim — but only if <paramref name="ownerId"/> still holds it (see
    /// <see cref="MarkAsProcessed"/> for why a lost claim is a no-op here too). Releasing the
    /// claim (clearing <see cref="OutboxMessage.ClaimedBy"/>/<see cref="OutboxMessage.ClaimedUntil"/>)
    /// makes the row immediately reclaimable by any dispatcher once <paramref name="nextAttemptOnUtc"/>
    /// elapses, instead of waiting out whatever remained of this pass's lease.
    /// </summary>
    /// <param name="ownerId">The owner id passed to the <see cref="ClaimPending"/> call that returned this row.</param>
    /// <param name="messageId">The identifier of the failed message.</param>
    /// <param name="error">A human-readable error message.</param>
    /// <param name="nextAttemptOnUtc">
    /// The UTC time at which <see cref="ClaimPending"/> may return this row again, or
    /// <see langword="null"/> to make it immediately eligible again. Callers should pass the
    /// backoff computed from the configured retry policy so a persistently failing row —
    /// including a poison row that can never succeed — does not busy-loop the dispatcher.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsFailed(string ownerId, Guid messageId, string error, DateTime? nextAttemptOnUtc, CancellationToken cancellationToken = default);
}
