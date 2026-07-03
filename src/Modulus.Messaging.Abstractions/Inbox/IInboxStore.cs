namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Stores incoming integration events and tracks per-handler consumption for idempotency.
/// Consumption is reservation-based: a handler's (message, handler) pair is claimed with
/// <see cref="TryReserve"/> before execution and marked complete with
/// <see cref="MarkConsumerProcessed"/> after, so concurrent duplicate deliveries execute a
/// handler at most once while a crashed owner's stale reservation can be taken over later.
/// </summary>
public interface IInboxStore
{
    Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InboxMessage>> GetPending(int batchSize, CancellationToken cancellationToken = default);
    Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Whether the handler has already <em>completed</em> this message (a live reservation does not count).</summary>
    Task<bool> HasBeenProcessed(Guid messageId, string handlerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the (message, handler) pair. Returns <c>false</c> when the pair is
    /// already processed or another delivery holds a reservation younger than
    /// <paramref name="staleAfter"/>; an older unprocessed reservation is taken over
    /// (single winner under concurrency) and the call returns <c>true</c>.
    /// </summary>
    Task<bool> TryReserve(Guid messageId, string handlerName, TimeSpan staleAfter, CancellationToken cancellationToken = default);

    /// <summary>Marks a reserved (message, handler) pair as successfully processed.</summary>
    Task MarkConsumerProcessed(Guid messageId, string handlerName, CancellationToken cancellationToken = default);
}
