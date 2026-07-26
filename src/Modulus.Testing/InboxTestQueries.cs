using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;

namespace Modulus.Testing;

/// <summary>
/// Assertion helpers over <see cref="InboxDbContext"/>, resolved from a fresh DI scope so a test
/// can assert against inbox state without hand-writing the scope-and-query boilerplate. Like
/// <see cref="OutboxTestQueries"/>, these return data rather than making assertion calls, so they
/// compose with any assertion library.
/// </summary>
/// <remarks>
/// <b>Read-only queries here (<see cref="GetInboxMessagesAsync"/>, <see cref="HasHandlerProcessedAsync"/>)
/// work against either EF Core provider.</b> But if a test needs to exercise <c>IInboxStore</c>'s
/// reservation/takeover contract directly (<c>TryReserve</c>, stale-reservation takeover,
/// <c>ReleaseReservation</c>), back <see cref="InboxDbContext"/> with
/// <c>UseSqlite("DataSource=:memory:")</c> instead of the EF Core in-memory provider — that
/// contract depends on the <c>InboxMessageConsumers</c> composite primary key actually being
/// enforced and on <c>ExecuteUpdateAsync</c> semantics the in-memory provider does not implement.
/// Keep the <c>SqliteConnection</c> open for the scope of the test (in-memory SQLite databases
/// are dropped when the last connection closes).
/// </remarks>
public static class InboxTestQueries
{
    /// <summary>Returns every inbox message row, oldest first (by <see cref="InboxMessage.OccurredOnUtc"/>).</summary>
    public static async Task<IReadOnlyList<InboxMessage>> GetInboxMessagesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxDbContext>();

        return await dbContext.InboxMessages
            .AsNoTracking()
            .OrderBy(m => m.OccurredOnUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns whether <paramref name="handlerFullName"/> has completed processing
    /// <paramref name="eventId"/> — a live (unfinished) reservation does not count, matching
    /// <c>IInboxStore.HasBeenProcessed</c>.
    /// </summary>
    /// <param name="eventId">The integration event's <c>EventId</c>.</param>
    /// <param name="handlerFullName">
    /// The handler's <see cref="Type.FullName"/> — the inbox idempotency key the consumer
    /// pipeline reserves and marks processed under, e.g.
    /// <c>typeof(OrderPlacedEventHandler).FullName!</c>.
    /// </param>
    public static async Task<bool> HasHandlerProcessedAsync(
        this IServiceProvider services,
        Guid eventId,
        string handlerFullName,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxDbContext>();

        return await dbContext.InboxMessageConsumers
            .AsNoTracking()
            .AnyAsync(
                c => c.InboxMessageId == eventId && c.Name == handlerFullName && c.ProcessedOnUtc != null,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
