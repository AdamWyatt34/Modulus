using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Outbox;

internal sealed class EfOutboxStore : IOutboxStore
{
    private readonly OutboxDbContext _dbContext;

    public EfOutboxStore(OutboxDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        var message = new OutboxMessage
        {
            Id = @event.EventId,
            EventType = @event.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(@event, @event.GetType()),
            CreatedAt = @event.OccurredOn
        };

        _dbContext.OutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPending(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsProcessed(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var messages = await _dbContext.OutboxMessages
            .Where(m => idList.Contains(m.Id))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var message in messages)
        {
            message.ProcessedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
