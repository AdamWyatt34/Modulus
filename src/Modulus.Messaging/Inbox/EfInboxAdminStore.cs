using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Inbox;

public sealed class EfInboxAdminStore(InboxDbContext dbContext) : IInboxAdminStore
{
    public async Task<int> CountOldAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        return await dbContext.InboxMessages
            .AsNoTracking()
            .CountAsync(m => m.OccurredOnUtc < olderThanUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> PurgeOldAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // Select-then-delete keeps the row-limited delete portable across providers (see
        // EfOutboxAdminStore.PurgeProcessedAsync) and lets the consumer rows — related by
        // convention, not by a mapped foreign key — be removed by id list in the same shape.
        var ids = await dbContext.InboxMessages
            .AsNoTracking()
            .Where(m => m.OccurredOnUtc < olderThanUtc)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Count == 0)
            return 0;

        // Consumer rows first: if the second delete is interrupted the leftover message row
        // simply gets re-purged next sweep, whereas orphaned consumer rows would linger forever.
        await dbContext.InboxMessageConsumers
            .Where(c => ids.Contains(c.InboxMessageId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext.InboxMessages
            .Where(m => ids.Contains(m.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
