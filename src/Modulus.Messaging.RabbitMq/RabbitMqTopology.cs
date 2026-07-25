using System.Text;
using Modulus.Messaging.Internals;

namespace Modulus.Messaging.RabbitMq;

/// <summary>
/// Pure naming conventions for the RabbitMQ topology:
/// a durable fanout exchange per event type, one durable queue per endpoint bound to every
/// subscribed exchange, and a per-endpoint dead-letter exchange and queue. Public so
/// operational tooling (e.g. <c>modulus dlq</c>) and user scripts can derive entity names.
/// </summary>
public static class RabbitMqTopology
{
    // RabbitMQ's hard limit on exchange and queue name length, in UTF-8 bytes.
    private const int MaxNameLengthBytes = 255;

    /// <summary>Exchange name for an event type: the lower-cased stable wire name.</summary>
    public static string ExchangeName(string messageTypeName)
        => EnsureWithinLimit(messageTypeName.ToLowerInvariant(), "exchange");

    /// <summary>The endpoint's consume queue. Replicas sharing the name compete for messages.</summary>
    public static string QueueName(string endpointName)
        => EnsureWithinLimit(EndpointNameResolver.Sanitize(endpointName), "queue");

    /// <summary>The endpoint's dead-letter exchange, targeted via <c>x-dead-letter-exchange</c>.</summary>
    public static string DeadLetterExchangeName(string endpointName)
        => EnsureWithinLimit($"{QueueName(endpointName)}.dlx", "dead-letter exchange");

    /// <summary>The queue bound to the dead-letter exchange.</summary>
    public static string DeadLetterQueueName(string endpointName)
        => EnsureWithinLimit($"{QueueName(endpointName)}.dead-letter", "dead-letter queue");

    /// <summary>
    /// Guards against RabbitMQ's 255-UTF-8-byte limit on exchange and queue names, surfacing a
    /// descriptive error at topology-resolution time instead of an opaque broker-side rejection
    /// on the first <c>exchange.declare</c>/<c>queue.declare</c>.
    /// </summary>
    private static string EnsureWithinLimit(string name, string entityKind)
    {
        var byteCount = Encoding.UTF8.GetByteCount(name);
        if (byteCount > MaxNameLengthBytes)
        {
            throw new InvalidOperationException(
                $"RabbitMQ {entityKind} name '{name}' is {byteCount} UTF-8 bytes, exceeding the " +
                $"broker's {MaxNameLengthBytes}-byte limit for exchange and queue names. Shorten " +
                "the event type's namespace/name or MessagingOptions.EndpointName.");
        }

        return name;
    }
}
