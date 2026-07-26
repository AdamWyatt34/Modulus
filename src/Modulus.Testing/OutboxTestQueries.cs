using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;

namespace Modulus.Testing;

/// <summary>
/// Assertion helpers over <see cref="OutboxDbContext"/>, resolved from a fresh DI scope so a test
/// can assert against outbox state without hand-writing the scope-and-query boilerplate. These
/// helpers are assertion-library-agnostic: they return data, not <c>bool</c>/assertion calls, so
/// they compose with Shouldly, xunit's own <c>Assert</c>, or anything else.
/// </summary>
/// <remarks>
/// <para>
/// The EF Core in-memory provider is fine for every query here — none of it depends on
/// transactions, raw SQL, or constraint enforcement. If you need reservation-semantics behavior
/// instead (that is <see cref="InboxTestQueries"/>'s territory), see its remarks for why SQLite
/// is required there instead.
/// </para>
/// <para>
/// Each helper opens its own DI scope, so a database name (and, for
/// <c>Microsoft.EntityFrameworkCore.InMemory</c>, an <c>InMemoryDatabaseRoot</c>) must be
/// captured <em>outside</em> the <c>AddDbContext</c> options delegate — that delegate's default
/// lifetime is Scoped, so a delegate that calls <c>Guid.NewGuid()</c> inline re-runs on every new
/// scope and silently points each one at a different, disjoint in-memory database:
/// <code>
/// var databaseName = Guid.NewGuid().ToString(); // captured once
/// var root = new InMemoryDatabaseRoot();          // captured once
/// services.AddDbContext&lt;OutboxDbContext&gt;(o => o.UseInMemoryDatabase(databaseName, root));
/// </code>
/// </para>
/// </remarks>
public static class OutboxTestQueries
{
    /// <summary>Returns every outbox row, oldest first, regardless of processed/attempt state.</summary>
    public static async Task<IReadOnlyList<OutboxMessage>> GetOutboxMessagesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        return await dbContext.OutboxMessages
            .AsNoTracking()
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns unprocessed outbox rows whose attempt count is below <paramref name="maxAttempts"/>
    /// and whose backoff/schedule has elapsed — the same population <c>IOutboxStore.GetPending</c>
    /// would dispatch next, oldest first.
    /// </summary>
    public static async Task<IReadOnlyList<OutboxMessage>> GetPendingOutboxMessagesAsync(
        this IServiceProvider services,
        int maxAttempts = 5,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var utcNow = DateTime.UtcNow;

        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null
                && m.Attempts < maxAttempts
                && (m.NextAttemptOnUtc == null || m.NextAttemptOnUtc <= utcNow)
                && (m.ScheduledOnUtc == null || m.ScheduledOnUtc <= utcNow))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns unprocessed outbox rows whose attempt count has reached <paramref name="maxAttempts"/>
    /// — the dead-lettered rows <c>modulus outbox list-failed</c> surfaces to an operator.
    /// </summary>
    public static async Task<IReadOnlyList<OutboxMessage>> GetDeadLetteredOutboxMessagesAsync(
        this IServiceProvider services,
        int maxAttempts = 5,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null && m.Attempts >= maxAttempts)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Polls until <see cref="GetPendingOutboxMessagesAsync"/> returns no rows, or
    /// <paramref name="timeout"/> (default 5 seconds, via <see cref="TestWait"/>) elapses. A row
    /// that reaches <paramref name="maxAttempts"/> and dead-letters no longer counts as pending,
    /// so a batch that is failing permanently drains from this wait's perspective even though it
    /// is stuck dead-lettered — check <see cref="GetDeadLetteredOutboxMessagesAsync"/> separately
    /// if that distinction matters to the test.
    /// </summary>
    public static Task WaitForOutboxDrainAsync(
        this IServiceProvider services,
        TimeSpan? timeout = null,
        int maxAttempts = 5,
        CancellationToken cancellationToken = default)
        => TestWait.WaitForConditionAsync(
            async () => (await services
                .GetPendingOutboxMessagesAsync(maxAttempts, cancellationToken)
                .ConfigureAwait(false)).Count == 0,
            timeout,
            because: "the outbox never drained its pending rows");
}
