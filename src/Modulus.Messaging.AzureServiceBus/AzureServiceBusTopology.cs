using System.Security.Cryptography;
using System.Text;
using Modulus.Messaging.Internals;

namespace Modulus.Messaging.AzureServiceBus;

/// <summary>
/// Pure naming conventions for the Azure Service Bus topology: a topic per event type and a
/// subscription per endpoint. Requires Standard or Premium tier — the Basic tier has no topics.
/// Public so operational tooling (e.g. <c>modulus dlq</c>) and user scripts can derive entity names.
/// </summary>
public static class AzureServiceBusTopology
{
    // Azure Service Bus limit for subscription names.
    private const int MaxSubscriptionNameLength = 50;

    // Azure Service Bus limit for topic (and queue) entity names.
    private const int MaxTopicNameLength = 260;

    /// <summary>
    /// Topic name for an event type: the lower-cased stable wire name (dots are legal). Any
    /// character illegal in a Service Bus entity name is folded to '.' — in practice this is
    /// just the '+' that <see cref="Type.FullName"/> uses to separate a nested type from its
    /// declaring type, which would otherwise surface as a broker-side 400 at publish time.
    /// </summary>
    public static string TopicName(string messageTypeName)
    {
        var sanitized = Sanitize(messageTypeName);

        if (sanitized.Length > MaxTopicNameLength)
        {
            throw new InvalidOperationException(
                $"Azure Service Bus topic name '{sanitized}' is {sanitized.Length} characters, " +
                $"exceeding the broker's {MaxTopicNameLength}-character limit for entity names. " +
                "Shorten the event type's namespace or name.");
        }

        return sanitized;
    }

    /// <summary>
    /// Subscription name for an endpoint. Names longer than the 50-character service limit are
    /// truncated with a stable 8-character hash suffix so distinct endpoints never collide.
    /// </summary>
    public static string SubscriptionName(string endpointName)
    {
        var sanitized = EndpointNameResolver.Sanitize(endpointName);

        if (sanitized.Length <= MaxSubscriptionNameLength)
            return sanitized;

        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(sanitized)))[..8];

        return $"{sanitized[..(MaxSubscriptionNameLength - 9)]}-{hash}";
    }

    /// <summary>
    /// Service Bus entity names allow only letters, digits, '.', '-', '_', and '/'; everything
    /// else is folded to '.' rather than left to surface as a broker-side rejection.
    /// </summary>
    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var ch in name.ToLowerInvariant())
        {
            builder.Append(ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_' or '/'
                ? ch
                : '.');
        }

        return builder.ToString();
    }
}
