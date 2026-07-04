using System.Text.Json;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Outbox;

internal sealed class EfOutboxStore(OutboxDbContext dbContext, IOutboxNotifier notifier) : IOutboxStore
{
    public async Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        var message = new OutboxMessage
        {
            Id = @event.EventId,
            EventType = @event.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(@event, @event.GetType()),
            CreatedAt = @event.OccurredOn
        };

        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Only signal when the row is already visible; inside a transaction the
        // commit-time notify comes from OutboxNotifyingInterceptor (auto-attached by
        // AddModulusOutbox), and coalescing absorbs the overlap when both fire.
        if (dbContext.Database.CurrentTransaction is null && Transaction.Current is null)
            notifier.Notify();
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPending(
        int batchSize,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null && m.Attempts < maxAttempts)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountPending(
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OutboxMessages
            .AsNoTracking()
            .CountAsync(m => m.ProcessedAt == null && m.Attempts < maxAttempts, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkAsProcessed(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        await dbContext.OutboxMessages
            .Where(m => idList.Contains(m.Id))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.ProcessedAt, DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);
    }

    // Single-writer assumption: the outbox processor is registered as a HostedService and
    // processes one batch sequentially. Multi-replica deployments should consider a partitioned
    // outbox or row-level locking instead of relying on this load-modify-save pattern.
    public async Task MarkAsFailed(
        Guid messageId,
        string error,
        CancellationToken cancellationToken = default)
    {
        var message = await dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken).ConfigureAwait(false);

        if (message is null)
            return;

        message.Attempts += 1;
        message.LastError = error;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
