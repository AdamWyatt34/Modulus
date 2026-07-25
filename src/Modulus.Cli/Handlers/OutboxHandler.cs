using System.Text.Json;
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
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new OutboxConnection(connectionString, provider);

        var path = configPath ?? Path.Combine(fileSystem.GetCurrentDirectory(), "appsettings.json");
        if (!fileSystem.FileExists(path))
        {
            console.WriteError($"Configuration file not found: {path}. Pass --connection-string explicitly.");
            return null;
        }

        try
        {
            var json = fileSystem.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings)
                && connectionStrings.TryGetProperty("Default", out var defaultCs)
                && defaultCs.GetString() is { } defaultValue
                && !string.IsNullOrWhiteSpace(defaultValue))
            {
                return new OutboxConnection(defaultValue, provider);
            }

            if (doc.RootElement.TryGetProperty("Messaging", out var messaging)
                && messaging.TryGetProperty("ConnectionString", out var legacyCs)
                && legacyCs.GetString() is { } legacyValue
                && !string.IsNullOrWhiteSpace(legacyValue))
            {
                console.WriteLine(
                    $"Warning: using Messaging:ConnectionString from '{path}' as the outbox database " +
                    "connection. That is the broker connection string, not the outbox's EF Core database — " +
                    "this fallback exists only for older configs. Add ConnectionStrings:Default (or pass " +
                    "--connection-string) instead.");
                return new OutboxConnection(legacyValue, provider);
            }

            console.WriteError($"'{path}' does not contain a ConnectionStrings:Default entry. Pass --connection-string explicitly, or add ConnectionStrings:Default to '{path}'.");
            return null;
        }
        catch (JsonException ex)
        {
            console.WriteError($"Failed to parse '{path}': {ex.Message}");
            return null;
        }
    }

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
