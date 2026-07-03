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

    /// <summary>Topic name for an event type: the lower-cased stable wire name (dots are legal).</summary>
    public static string TopicName(string messageTypeName)
        => messageTypeName.ToLowerInvariant();

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
}
