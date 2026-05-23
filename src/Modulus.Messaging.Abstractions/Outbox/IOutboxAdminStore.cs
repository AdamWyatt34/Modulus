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
}
