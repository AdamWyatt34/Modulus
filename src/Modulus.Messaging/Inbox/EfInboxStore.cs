using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Inbox;

internal sealed class EfInboxStore : IInboxStore
{
    private readonly InboxDbContext _dbContext;

    public EfInboxStore(InboxDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.InboxMessages
            .AnyAsync(m => m.Id == @event.EventId, cancellationToken);

        if (exists)
        {
            return;
        }

        var message = new InboxMessage
        {
            Id = @event.EventId,
            Type = @event.GetType().AssemblyQualifiedName!,
            Content = JsonSerializer.Serialize(@event, @event.GetType()),
            OccurredOnUtc = @event.OccurredOn,
        };

        _dbContext.InboxMessages.Add(message);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent insert of the same message — safe to ignore
            _dbContext.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<InboxMessage>> GetPending(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.InboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsProcessed(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var messages = await _dbContext.InboxMessages
            .Where(m => idList.Contains(m.Id))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var message in messages)
        {
            message.ProcessedOnUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasBeenProcessed(
        Guid messageId,
        string handlerName,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.InboxMessageConsumers
            .AnyAsync(c => c.InboxMessageId == messageId && c.Name == handlerName, cancellationToken);
    }

    public async Task RecordConsumer(
        Guid messageId,
        string handlerName,
        CancellationToken cancellationToken = default)
    {
        _dbContext.InboxMessageConsumers.Add(new InboxMessageConsumer
        {
            InboxMessageId = messageId,
            Name = handlerName,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
