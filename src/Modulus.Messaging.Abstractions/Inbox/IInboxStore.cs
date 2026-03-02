namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Stores incoming integration events and tracks per-handler consumption for idempotency.
/// </summary>
public interface IInboxStore
{
    Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InboxMessage>> GetPending(int batchSize, CancellationToken cancellationToken = default);
    Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<bool> HasBeenProcessed(Guid messageId, string handlerName, CancellationToken cancellationToken = default);
    Task RecordConsumer(Guid messageId, string handlerName, CancellationToken cancellationToken = default);
}
