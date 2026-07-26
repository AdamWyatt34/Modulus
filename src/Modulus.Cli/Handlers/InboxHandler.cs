using Microsoft.EntityFrameworkCore;
using Modulus.Cli.Infrastructure;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;

namespace Modulus.Cli.Handlers;

public sealed class InboxHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    Func<OutboxConnection, IInboxAdminSession> sessionFactory)
{
    public async Task<int> PurgeAsync(
        OutboxConnection connection,
        int olderThanDays,
        int batchSize,
        bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (olderThanDays < 1)
        {
            // Zero would purge rows the broker may still redeliver — the inbox is the
            // deduplication window, so an age floor of one day keeps the footgun small.
            console.WriteError("--older-than-days must be at least 1 for the inbox: purged rows leave the deduplication window.");
            return 1;
        }

        if (batchSize is <= 0 or > 10_000)
        {
            console.WriteError("--batch-size must be between 1 and 10000.");
            return 1;
        }

        var cutoffUtc = DateTime.UtcNow.AddDays(-olderThanDays);

        IInboxAdminSession? session = null;
        try
        {
            session = sessionFactory(connection);

            if (!confirm)
            {
                var count = await session.Store.CountOldAsync(cutoffUtc, cancellationToken);
                console.WriteLine(
                    $"{count} inbox message(s) older than {olderThanDays} day(s) " +
                    $"(occurred before {cutoffUtc:yyyy-MM-dd HH:mm:ss} UTC) would be purged, including their per-handler consumer rows.");
                console.WriteLine(
                    "Re-run with --confirm to delete them. Purged rows leave the deduplication window — " +
                    "only purge past your broker's maximum redelivery horizon (dead-letter replays included).");
                return 0;
            }

            long total = 0;
            int purged;
            do
            {
                purged = await session.Store.PurgeOldAsync(cutoffUtc, batchSize, cancellationToken);
                total += purged;
            }
            while (purged >= batchSize);

            console.WriteSuccess($"Purged {total} inbox message(s) older than {olderThanDays} day(s).");
            return 0;
        }
        catch (Exception ex)
        {
            console.WriteError($"Failed to purge the inbox database: {ex.Message}");
            return 1;
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
        }
    }

    /// <summary>Same resolution order as <see cref="OutboxHandler.ResolveConnection"/>; the inbox lives in the same database wiring.</summary>
    public OutboxConnection? ResolveConnection(string? connectionString, string? configPath, OutboxProvider provider)
        => MessagingDatabaseResolver.Resolve(fileSystem, console, connectionString, configPath, provider, "inbox");
}

/// <summary>
/// Bundles an <see cref="IInboxAdminStore"/> with the lifetime of the underlying database
/// context. Disposing the session disposes the context.
/// </summary>
public interface IInboxAdminSession : IAsyncDisposable
{
    IInboxAdminStore Store { get; }
}

internal sealed class EfInboxAdminSession(InboxDbContext dbContext) : IInboxAdminSession
{
    public IInboxAdminStore Store { get; } = new EfInboxAdminStore(dbContext);
    public ValueTask DisposeAsync() => dbContext.DisposeAsync();
}

internal static class InboxStoreFactory
{
    public static IInboxAdminSession Create(OutboxConnection connection)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InboxDbContext>();
        _ = connection.Provider switch
        {
            OutboxProvider.SqlServer => optionsBuilder.UseSqlServer(connection.ConnectionString),
            OutboxProvider.Sqlite => optionsBuilder.UseSqlite(connection.ConnectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(connection)),
        };

        return new EfInboxAdminSession(new InboxDbContext(optionsBuilder.Options));
    }
}
