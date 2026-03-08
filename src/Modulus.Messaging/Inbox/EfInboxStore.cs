using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Inbox;

internal sealed class EfInboxStore(InboxDbContext dbContext) : IInboxStore
{
    public async Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.InboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.Id == @event.EventId, cancellationToken).ConfigureAwait(false);

        if (exists)
            return;

        var message = new InboxMessage
        {
            Id = @event.EventId,
            Type = @event.GetType().AssemblyQualifiedName!,
            Content = JsonSerializer.Serialize(@event, @event.GetType()),
            OccurredOnUtc = @event.OccurredOn,
        };

        dbContext.InboxMessages.Add(message);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Concurrent insert of the same message — safe to ignore
            dbContext.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<InboxMessage>> GetPending(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.InboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkAsProcessed(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        await dbContext.InboxMessages
            .Where(m => idList.Contains(m.Id))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.ProcessedOnUtc, DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasBeenProcessed(
        Guid messageId,
        string handlerName,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.InboxMessageConsumers
            .AsNoTracking()
            .AnyAsync(c => c.InboxMessageId == messageId && c.Name == handlerName, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordConsumer(
        Guid messageId,
        string handlerName,
        CancellationToken cancellationToken = default)
    {
        dbContext.InboxMessageConsumers.Add(new InboxMessageConsumer
        {
            InboxMessageId = messageId,
            Name = handlerName,
        });

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
