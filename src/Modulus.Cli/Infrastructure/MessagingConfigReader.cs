using System.Text.Json;

namespace Modulus.Cli.Infrastructure;

/// <summary>The subset of the appsettings <c>Messaging</c> section the CLI's operational commands need.</summary>
public sealed record MessagingConfig(string? ConnectionString, string? Transport, string? EndpointName);

/// <summary>
/// Reads the <c>Messaging</c> section from an appsettings.json file. Shared by the outbox and
/// dlq commands so connection resolution behaves identically across operational tooling.
/// </summary>
public static class MessagingConfigReader
{
    /// <summary>
    /// Returns the parsed section, or null with an error written to <paramref name="console"/>
    /// when the file is missing, malformed, or has no <c>Messaging</c> section.
    /// </summary>
    public static MessagingConfig? Read(IFileSystem fileSystem, IConsoleOutput console, string? configPath)
    {
        var path = configPath ?? Path.Combine(fileSystem.GetCurrentDirectory(), "appsettings.json");
        if (!fileSystem.FileExists(path))
        {
            console.WriteError($"Configuration file not found: {path}. Pass --connection-string explicitly.");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(fileSystem.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Messaging", out var messaging))
            {
                console.WriteError($"'{path}' does not contain a Messaging section.");
                return null;
            }

            return new MessagingConfig(
                ReadString(messaging, "ConnectionString"),
                ReadString(messaging, "Transport"),
                ReadString(messaging, "EndpointName"));
        }
        catch (JsonException ex)
        {
            console.WriteError($"Failed to parse '{path}': {ex.Message}");
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
