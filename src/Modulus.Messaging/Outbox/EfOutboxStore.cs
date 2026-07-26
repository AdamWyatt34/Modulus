using System.Diagnostics;
using System.Text.Json;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Outbox;

internal sealed class EfOutboxStore(OutboxDbContext dbContext, IOutboxNotifier notifier) : IOutboxStore
{
    public Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
        => SaveCore(@event, scheduledOnUtc: null, cancellationToken);

    public Task Save(IIntegrationEvent @event, DateTimeOffset enqueueAtUtc, CancellationToken cancellationToken = default)
        => SaveCore(@event, enqueueAtUtc.UtcDateTime, cancellationToken);

    private async Task SaveCore(IIntegrationEvent @event, DateTime? scheduledOnUtc, CancellationToken cancellationToken)
    {
        // Save runs inside the caller's request flow, so the ambient activity is the business
        // operation — captured on the row so the (much later) dispatch can link back to it.
        var activity = Activity.Current;

        var message = new OutboxMessage
        {
            Id = @event.EventId,
            EventType = @event.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(@event, @event.GetType()),
            CreatedAt = @event.OccurredOn,
            ScheduledOnUtc = scheduledOnUtc,
            TraceParent = activity?.Id,
            TraceState = string.IsNullOrEmpty(activity?.TraceStateString) ? null : activity.TraceStateString,
        };

        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Only signal when the row is already visible; inside a transaction the
        // commit-time notify comes from OutboxNotifyingInterceptor (auto-attached by
        // AddModulusOutbox), and coalescing absorbs the overlap when both fire.
        if (dbContext.Database.CurrentTransaction is null && Transaction.Current is null)
            notifier.Notify();
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimPending(
        string ownerId,
        TimeSpan lease,
        int batchSize,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        // Step 1: find candidates without taking a lock on them yet. AsNoTracking + Select(Id)
        // keeps this a cheap read; the eligibility predicate is identical to pre-4.0 GetPending
        // plus the claim clause (unclaimed, or a lease that has already expired).
        var candidateIds = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null
                && m.Attempts < maxAttempts
                && (m.NextAttemptOnUtc == null || m.NextAttemptOnUtc <= utcNow)
                && (m.ScheduledOnUtc == null || m.ScheduledOnUtc <= utcNow)
                && (m.ClaimedUntil == null || m.ClaimedUntil < utcNow))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (candidateIds.Count == 0)
            return [];

        // Step 2: atomic claim. The WHERE repeats the unclaimed-or-expired clause, evaluated
        // fresh against the current row state — not the step-1 snapshot — so a competitor that
        // claimed one of these ids in between just drops that row out of this UPDATE's match
        // set. This is what makes each row single-winner under concurrency without any
        // provider-specific locking hint: whichever claimant's UPDATE actually commits first for
        // a given row wins it; every later UPDATE (from any owner, including this one re-running)
        // simply no longer matches it.
        var claimedUntil = utcNow + lease;
        await dbContext.OutboxMessages
            .Where(m => candidateIds.Contains(m.Id) && (m.ClaimedUntil == null || m.ClaimedUntil < utcNow))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.ClaimedBy, ownerId).SetProperty(m => m.ClaimedUntil, claimedUntil),
                cancellationToken).ConfigureAwait(false);

        // Step 3: re-fetch only the rows this call actually won. A candidate lost to a
        // competitor between steps 1 and 2 does not match ClaimedBy == ownerId here and is
        // silently absent — that is exactly the desired outcome, not an error.
        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => candidateIds.Contains(m.Id) && m.ClaimedBy == ownerId && m.ClaimedUntil > utcNow)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountPending(
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        // Future-scheduled rows are deliberately excluded: unlike retry backoff (which is
        // outstanding work pushed back by failures), a not-yet-due scheduled message is not
        // backlog and must not trip the backlog health check.
        var utcNow = DateTime.UtcNow;

        return await dbContext.OutboxMessages
            .AsNoTracking()
            .CountAsync(
                m => m.ProcessedAt == null
                    && m.Attempts < maxAttempts
                    && (m.ScheduledOnUtc == null || m.ScheduledOnUtc <= utcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkAsProcessed(
        string ownerId,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();

        // ClaimedBy == ownerId guards against a lease takeover: if this pass ran long enough
        // that another owner reclaimed one of these rows in the meantime, that row is no
        // longer this owner's to stamp — it silently drops out of the affected set instead of
        // being marked processed on the loser's say-so.
        await dbContext.OutboxMessages
            .Where(m => idList.Contains(m.Id) && m.ClaimedBy == ownerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.ProcessedAt, DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkAsFailed(
        string ownerId,
        Guid messageId,
        string error,
        DateTime? nextAttemptOnUtc,
        CancellationToken cancellationToken = default)
    {
        // Single set-based ExecuteUpdate instead of the old load-modify-save: the previous
        // pattern raced Attempts under multi-instance polling (two readers loading the same row,
        // both incrementing from the same stale value, one increment silently lost). The
        // ClaimedBy guard is the same lease-takeover protection as MarkAsProcessed; clearing the
        // claim (rather than leaving it to expire) makes a durably-recorded failure immediately
        // reclaimable once nextAttemptOnUtc elapses, instead of idling out the rest of this
        // pass's lease first.
        await dbContext.OutboxMessages
            .Where(m => m.Id == messageId && m.ClaimedBy == ownerId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                    .SetProperty(m => m.LastError, error)
                    .SetProperty(m => m.NextAttemptOnUtc, nextAttemptOnUtc)
                    .SetProperty(m => m.ClaimedBy, (string?)null)
                    .SetProperty(m => m.ClaimedUntil, (DateTime?)null),
                cancellationToken).ConfigureAwait(false);
    }
}
