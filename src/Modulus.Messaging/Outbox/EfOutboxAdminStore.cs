using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Outbox;

public sealed class EfOutboxAdminStore(OutboxDbContext dbContext) : IOutboxAdminStore
{
    public async Task<IReadOnlyList<OutboxMessage>> GetFailedAsync(
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null && m.Attempts >= maxAttempts)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RetryAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
            return false;

        message.Attempts = 0;
        message.LastError = null;
        // Clear any pending backoff too: a message an operator explicitly retries must be
        // eligible for ClaimPending on the very next poll, not still serving out the wait from
        // whatever attempt originally dead-lettered it.
        message.NextAttemptOnUtc = null;
        // Clear any stale claim too: a dead-lettered row's claim was already released by
        // MarkAsFailed, but a row an operator retries mid-flight (still claimed by a live
        // dispatcher instance) must not stay locked out of the next poll for the rest of that
        // instance's lease — an explicit operator retry always wins over an in-flight claim.
        message.ClaimedBy = null;
        message.ClaimedUntil = null;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> PurgeAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
            return false;

        dbContext.OutboxMessages.Remove(message);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<int> CountProcessedAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        return await dbContext.OutboxMessages
            .AsNoTracking()
            .CountAsync(m => m.ProcessedAt != null && m.ProcessedAt < olderThanUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> PurgeProcessedAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // Select-then-delete instead of ExecuteDelete-with-Take: not every relational provider
        // can translate a row-limited DELETE (SQLite needs a non-default build flag), and the
        // id list keeps each round trip's lock footprint bounded to one batch.
        var ids = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < olderThanUtc)
            .OrderBy(m => m.ProcessedAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Count == 0)
            return 0;

        return await dbContext.OutboxMessages
            .Where(m => ids.Contains(m.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
