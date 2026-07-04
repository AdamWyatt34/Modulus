using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Outbox;

/// <summary>
/// EF Core interceptor that wakes the outbox processor the moment committed
/// <see cref="OutboxMessage"/> rows become visible, so they are dispatched immediately
/// instead of waiting for the poll interval. Attach it to any <see cref="DbContext"/>
/// that maps the outbox table (the scaffolded module contexts do this out of the box):
/// <code>
/// services.AddDbContext&lt;AppDbContext&gt;((sp, options) =&gt;
/// {
///     options.UseSqlServer(connectionString);
///     options.AddInterceptors(sp.GetRequiredService&lt;OutboxNotifyingInterceptor&gt;());
/// });
/// </code>
/// </summary>
/// <remarks>
/// Saves inside an EF-managed transaction notify when the transaction commits (a save-time
/// notify would fire before the rows are visible and be lost on rollback). Transactions EF
/// does not observe — an externally-owned transaction passed to <c>Database.UseTransaction</c>,
/// or an ambient <see cref="TransactionScope"/> — raise no commit callback; rows written under
/// them are picked up by the poll-interval fallback sweep instead.
/// </remarks>
public sealed class OutboxNotifyingInterceptor(IOutboxNotifier notifier)
    : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    // One interceptor instance is shared by every DbContext it is attached to, so
    // per-save/per-transaction flags must be keyed by context. A DbContext is
    // single-threaded by contract; the table itself is thread-safe across contexts
    // and weakly keyed, so an abandoned context leaks no entry.
    private readonly ConditionalWeakTable<DbContext, State> _state = new();

    private sealed class State
    {
        public bool HasNewOutboxRows;
        public bool NotifyOnCommit;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        DetectOutboxRows(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        DetectOutboxRows(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyOrDefer(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        NotifyOrDefer(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData)
        => ClearPendingSave(eventData.Context);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ClearPendingSave(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => NotifyIfDeferred(eventData.Context);

    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        NotifyIfDeferred(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => ClearAll(eventData.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ClearAll(eventData.Context);
        return Task.CompletedTask;
    }

    private void DetectOutboxRows(DbContext? context)
    {
        if (context is null)
            return;

        // Detection must happen before the save — afterwards the entries are Unchanged
        // and indistinguishable from previously loaded rows.
        foreach (var entry in context.ChangeTracker.Entries<OutboxMessage>())
        {
            if (entry.State == EntityState.Added)
            {
                _state.GetOrCreateValue(context).HasNewOutboxRows = true;
                return;
            }
        }
    }

    private void NotifyOrDefer(DbContext? context)
    {
        if (context is null || !_state.TryGetValue(context, out var state) || !state.HasNewOutboxRows)
            return;

        state.HasNewOutboxRows = false;

        if (context.Database.CurrentTransaction is null && Transaction.Current is null)
            notifier.Notify();
        else
            state.NotifyOnCommit = true;
    }

    private void NotifyIfDeferred(DbContext? context)
    {
        if (context is null || !_state.TryGetValue(context, out var state))
            return;

        var shouldNotify = state.NotifyOnCommit;
        ClearAll(context);

        if (shouldNotify)
            notifier.Notify();
    }

    private void ClearPendingSave(DbContext? context)
    {
        // Keep NotifyOnCommit: an earlier successful save in the same transaction is
        // still commit-eligible even when a later save fails.
        if (context is not null && _state.TryGetValue(context, out var state))
            state.HasNewOutboxRows = false;
    }

    private void ClearAll(DbContext? context)
    {
        if (context is not null)
            _state.Remove(context);
    }
}
