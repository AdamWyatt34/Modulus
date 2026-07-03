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
            .AnyAsync(
                c => c.InboxMessageId == messageId && c.Name == handlerName && c.ProcessedOnUtc != null,
                cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryReserve(
        Guid messageId,
        string handlerName,
        TimeSpan staleAfter,
        CancellationToken cancellationToken = default)
    {
        var reservation = new InboxMessageConsumer
        {
            InboxMessageId = messageId,
            Name = handlerName,
            ReservedOnUtc = DateTime.UtcNow,
        };
        dbContext.InboxMessageConsumers.Add(reservation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Detach so a later claim through this same context reaches the database's PK
            // instead of tripping over the change tracker's identity map.
            dbContext.Entry(reservation).State = EntityState.Detached;
            return true;
        }
        catch (DbUpdateException)
        {
            // The composite PK claimed the pair for someone else — fall through to takeover.
            dbContext.ChangeTracker.Clear();
        }

        // The row exists: processed, freshly reserved, or abandoned. The predicate makes
        // takeover of an abandoned reservation single-winner — a concurrent takeover moves
        // ReservedOnUtc past the cutoff, so the loser's update matches zero rows.
        var cutoff = DateTime.UtcNow - staleAfter;
        var takenOver = await dbContext.InboxMessageConsumers
            .Where(c => c.InboxMessageId == messageId
                && c.Name == handlerName
                && c.ProcessedOnUtc == null
                && c.ReservedOnUtc < cutoff)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.ReservedOnUtc, DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);

        return takenOver == 1;
    }

    public async Task MarkConsumerProcessed(
        Guid messageId,
        string handlerName,
        CancellationToken cancellationToken = default)
    {
        await dbContext.InboxMessageConsumers
            .Where(c => c.InboxMessageId == messageId && c.Name == handlerName)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.ProcessedOnUtc, DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);
    }
}
