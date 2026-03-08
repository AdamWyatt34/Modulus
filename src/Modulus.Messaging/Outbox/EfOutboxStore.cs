using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Outbox;

internal sealed class EfOutboxStore(OutboxDbContext dbContext) : IOutboxStore
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
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPending(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
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
}
