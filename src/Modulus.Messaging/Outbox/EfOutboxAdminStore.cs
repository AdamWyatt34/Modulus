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
}
