using System.Text.Json;
using Modulus.Cli.Handlers;

namespace Modulus.Cli.Infrastructure;

/// <summary>
/// Shared connection-string resolution for the messaging database commands
/// (<c>modulus outbox</c>, <c>modulus inbox</c>): an explicit flag first, then
/// <c>ConnectionStrings:Default</c> from the config file, then — with a loud warning —
/// the legacy <c>Messaging:ConnectionString</c> broker-string fallback.
/// </summary>
internal static class MessagingDatabaseResolver
{
    public static OutboxConnection? Resolve(
        IFileSystem fileSystem,
        IConsoleOutput console,
        string? connectionString,
        string? configPath,
        OutboxProvider provider,
        string storeName)
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
                    $"Warning: using Messaging:ConnectionString from '{path}' as the {storeName} database " +
                    $"connection. That is the broker connection string, not the {storeName}'s EF Core database — " +
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
}
