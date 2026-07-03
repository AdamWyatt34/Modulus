namespace Modulus.Messaging.Transports;

/// <summary>
/// The broker abstraction Modulus messaging runs on. Implementations own connections,
/// topology (exchanges/topics/queues), and delivery mechanics; the consumer pipeline
/// owns deserialization, idempotency, and retry, reporting a
/// <see cref="MessageDispatchResult"/> per message.
/// </summary>
public interface IMessageTransport : IAsyncDisposable
{
    /// <summary>Publishes an event envelope to all subscribers of its message type.</summary>
    Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provisions topology for the given subscriptions and begins delivering messages to
    /// <paramref name="onMessage"/>. Delivery continues until <see cref="StopConsumingAsync"/>.
    /// </summary>
    Task StartConsumingAsync(
        IReadOnlyList<TransportSubscription> subscriptions,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken cancellationToken = default);

    /// <summary>Stops delivery and drains in-flight messages.</summary>
    Task StopConsumingAsync(CancellationToken cancellationToken = default);
}
