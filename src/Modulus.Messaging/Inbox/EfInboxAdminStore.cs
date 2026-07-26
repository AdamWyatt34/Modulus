using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Inbox;

public sealed class EfInboxAdminStore(InboxDbContext dbContext) : IInboxAdminStore
{
    /// <summary>
    /// A message with an unprocessed reservation younger than this is considered in flight
    /// and is never purged, whatever its age: deleting a live reservation would let a
    /// concurrent duplicate delivery re-reserve and run the handler in parallel — the exact
    /// invariant the reservation system protects. Sized far above any sane
    /// <see cref="MessagingOptions.ConsumerReservationTimeout"/> (default 5 minutes); a
    /// reservation older than this is a crashed owner's leftover and is safe to remove.
    /// </summary>
    internal static readonly TimeSpan ActiveReservationGrace = TimeSpan.FromHours(1);

    public async Task<int> CountOldAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        var reservationCutoff = DateTime.UtcNow - ActiveReservationGrace;

        return await PurgeCandidates(olderThanUtc, reservationCutoff)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> PurgeOldAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var reservationCutoff = DateTime.UtcNow - ActiveReservationGrace;

        // Select-then-delete keeps the row-limited delete portable across providers (see
        // EfOutboxAdminStore.PurgeProcessedAsync) and lets the consumer rows — related by
        // convention, not by a mapped foreign key — be removed by id list in the same shape.
        var ids = await PurgeCandidates(olderThanUtc, reservationCutoff)
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

    private IQueryable<InboxMessage> PurgeCandidates(DateTime olderThanUtc, DateTime reservationCutoff)
        => dbContext.InboxMessages
            .AsNoTracking()
            .Where(m => m.OccurredOnUtc < olderThanUtc)
            .Where(m => !dbContext.InboxMessageConsumers.Any(
                c => c.InboxMessageId == m.Id
                    && c.ProcessedOnUtc == null
                    && c.ReservedOnUtc >= reservationCutoff));
}
