using Microsoft.EntityFrameworkCore;
using Modulus.Cli.Infrastructure;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;

namespace Modulus.Cli.Handlers;

public sealed class OutboxHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    Func<OutboxConnection, IOutboxAdminSession> sessionFactory)
{
    public async Task<int> ListFailedAsync(OutboxConnection connection, int maxAttempts, CancellationToken cancellationToken = default)
    {
        IOutboxAdminSession? session = null;
        try
        {
            session = sessionFactory(connection);
            var failed = await session.Store.GetFailedAsync(maxAttempts, cancellationToken);

            if (failed.Count == 0)
            {
                console.WriteLine("No failed outbox messages.");
                return 0;
            }

            console.WriteLine($"Failed outbox messages (>= {maxAttempts} attempts): {failed.Count}");
            console.WriteLine("");
            console.WriteLine($"{"Id",-38} {"Attempts",-9} {"CreatedAt (UTC)",-21} {"EventType",-40} LastError");
            console.WriteLine(new string('-', 140));

            foreach (var msg in failed)
            {
                var truncatedError = Truncate(msg.LastError ?? "", 60);
                var shortType = ExtractShortTypeName(msg.EventType);
                var createdAt = msg.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
                console.WriteLine($"{msg.Id,-38} {msg.Attempts,-9} {createdAt,-21} {Truncate(shortType, 40),-40} {truncatedError}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            console.WriteError($"Failed to read the outbox database: {ex.Message}");
            return 1;
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
        }
    }

    public async Task<int> RetryAsync(OutboxConnection connection, Guid messageId, CancellationToken cancellationToken = default)
    {
        IOutboxAdminSession? session = null;
        try
        {
            session = sessionFactory(connection);
            var success = await session.Store.RetryAsync(messageId, cancellationToken);

            if (!success)
            {
                console.WriteError($"Outbox message '{messageId}' not found.");
                return 1;
            }

            console.WriteSuccess($"Outbox message '{messageId}' reset. Attempts cleared; the outbox processor will retry on the next poll.");
            return 0;
        }
        catch (Exception ex)
        {
            console.WriteError($"Failed to reach the outbox database: {ex.Message}");
            return 1;
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
        }
    }

    public async Task<int> PurgeAsync(OutboxConnection connection, Guid messageId, CancellationToken cancellationToken = default)
    {
        IOutboxAdminSession? session = null;
        try
        {
            session = sessionFactory(connection);
            var success = await session.Store.PurgeAsync(messageId, cancellationToken);

            if (!success)
            {
                console.WriteError($"Outbox message '{messageId}' not found.");
                return 1;
            }

            console.WriteSuccess($"Outbox message '{messageId}' purged.");
            return 0;
        }
        catch (Exception ex)
        {
            console.WriteError($"Failed to reach the outbox database: {ex.Message}");
            return 1;
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
        }
    }

    public async Task<int> PurgeProcessedAsync(
        OutboxConnection connection,
        int olderThanDays,
        int batchSize,
        bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (olderThanDays < 0)
        {
            console.WriteError("--older-than-days must be zero or greater.");
            return 1;
        }

        if (batchSize is <= 0 or > 10_000)
        {
            console.WriteError("--batch-size must be between 1 and 10000.");
            return 1;
        }

        var cutoffUtc = DateTime.UtcNow.AddDays(-olderThanDays);

        IOutboxAdminSession? session = null;
        try
        {
            session = sessionFactory(connection);

            if (!confirm)
            {
                var count = await session.Store.CountProcessedAsync(cutoffUtc, cancellationToken);
                console.WriteLine(
                    $"{count} processed outbox message(s) older than {olderThanDays} day(s) " +
                    $"(processed before {cutoffUtc:yyyy-MM-dd HH:mm:ss} UTC) would be purged.");
                console.WriteLine("Re-run with --confirm to delete them. Unprocessed and dead-lettered rows are never touched.");
                return 0;
            }

            long total = 0;
            int purged;
            do
            {
                purged = await session.Store.PurgeProcessedAsync(cutoffUtc, batchSize, cancellationToken);
                total += purged;
            }
            while (purged >= batchSize);

            console.WriteSuccess(
                $"Purged {total} processed outbox message(s) older than {olderThanDays} day(s).");
            return 0;
        }
        catch (Exception ex)
        {
            console.WriteError($"Failed to purge the outbox database: {ex.Message}");
            return 1;
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
        }
    }

    /// <summary>
    /// Resolution order: an explicit <c>--connection-string</c> flag, then
    /// <c>ConnectionStrings:Default</c> in the config file (the outbox is an EF Core database,
    /// wired the same way every scaffolded DbContext is), then — only as a legacy fallback, with
    /// a loud warning — <c>Messaging:ConnectionString</c>, which is actually the *broker*
    /// connection string. Reading only the broker string used to be the sole path, so a fresh
    /// scaffold with no `Messaging:ConnectionString` (the common case: InMemory transport, or a
    /// message broker's string that isn't a SQL connection string at all) would either fail to
    /// resolve or, worse, hand `amqp://...`/a Service Bus namespace string to `UseSqlServer`.
    /// </summary>
    public OutboxConnection? ResolveConnection(string? connectionString, string? configPath, OutboxProvider provider)
        => MessagingDatabaseResolver.Resolve(fileSystem, console, connectionString, configPath, provider, "outbox");

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    private static string ExtractShortTypeName(string assemblyQualifiedName)
    {
        var commaIndex = assemblyQualifiedName.IndexOf(',');
        var fullName = commaIndex > 0 ? assemblyQualifiedName[..commaIndex] : assemblyQualifiedName;
        var dotIndex = fullName.LastIndexOf('.');
        return dotIndex > 0 ? fullName[(dotIndex + 1)..] : fullName;
    }
}

public sealed record OutboxConnection(string ConnectionString, OutboxProvider Provider);

public enum OutboxProvider
{
    SqlServer,
    Sqlite,
}

/// <summary>
/// Bundles an <see cref="IOutboxAdminStore"/> with the lifetime of the underlying database
/// context. Disposing the session disposes the context.
/// </summary>
public interface IOutboxAdminSession : IAsyncDisposable
{
    IOutboxAdminStore Store { get; }
}

internal sealed class EfOutboxAdminSession(OutboxDbContext dbContext) : IOutboxAdminSession
{
    public IOutboxAdminStore Store { get; } = new EfOutboxAdminStore(dbContext);
    public ValueTask DisposeAsync() => dbContext.DisposeAsync();
}

internal static class OutboxStoreFactory
{
    public static IOutboxAdminSession Create(OutboxConnection connection)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OutboxDbContext>();
        _ = connection.Provider switch
        {
            OutboxProvider.SqlServer => optionsBuilder.UseSqlServer(connection.ConnectionString),
            OutboxProvider.Sqlite => optionsBuilder.UseSqlite(connection.ConnectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(connection)),
        };

        return new EfOutboxAdminSession(new OutboxDbContext(optionsBuilder.Options));
    }
}
