using System.Reflection;
using System.Text;

namespace Modulus.Messaging.Internals;

/// <summary>
/// Resolves the effective endpoint name for a host: the configured
/// <see cref="MessagingOptions.EndpointName"/>, or the entry assembly name lower-cased
/// and sanitized to characters safe for both RabbitMQ queue names and Azure Service Bus
/// subscription names (letters, digits, '.', '-', '_').
/// </summary>
internal static class EndpointNameResolver
{
    public static string Resolve(MessagingOptions options)
    {
        var raw = !string.IsNullOrWhiteSpace(options.EndpointName)
            ? options.EndpointName
            : Assembly.GetEntryAssembly()?.GetName().Name ?? "modulus-app";

        return Sanitize(raw);
    }

    public static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var ch in name.ToLowerInvariant())
        {
            builder.Append(ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_'
                ? ch
                : '-');
        }

        return builder.Length == 0 ? "modulus-app" : builder.ToString();
    }
}
