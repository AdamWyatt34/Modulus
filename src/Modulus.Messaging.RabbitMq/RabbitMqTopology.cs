using Modulus.Messaging.Internals;

namespace Modulus.Messaging.RabbitMq;

/// <summary>
/// Pure naming conventions for the RabbitMQ topology:
/// a durable fanout exchange per event type, one durable queue per endpoint bound to every
/// subscribed exchange, and a per-endpoint dead-letter exchange and queue.
/// </summary>
internal static class RabbitMqTopology
{
    /// <summary>Exchange name for an event type: the lower-cased stable wire name.</summary>
    public static string ExchangeName(string messageTypeName)
        => messageTypeName.ToLowerInvariant();

    /// <summary>The endpoint's consume queue. Replicas sharing the name compete for messages.</summary>
    public static string QueueName(string endpointName)
        => EndpointNameResolver.Sanitize(endpointName);

    /// <summary>The endpoint's dead-letter exchange, targeted via <c>x-dead-letter-exchange</c>.</summary>
    public static string DeadLetterExchangeName(string endpointName)
        => $"{QueueName(endpointName)}.dlx";

    /// <summary>The queue bound to the dead-letter exchange.</summary>
    public static string DeadLetterQueueName(string endpointName)
        => $"{QueueName(endpointName)}.dead-letter";
}
