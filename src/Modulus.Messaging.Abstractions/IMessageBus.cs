namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Publishes integration events across module boundaries.
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publishes an integration event to all subscribed handlers.
    /// </summary>
    /// <typeparam name="TEvent">The type of integration event.</typeparam>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;

    /// <summary>
    /// Publishes an integration event that the broker holds until
    /// <paramref name="enqueueAtUtc"/> before delivering to subscribers. Azure Service Bus
    /// schedules natively; RabbitMQ routes through a per-event-type TTL queue (see the
    /// transport docs for the head-of-queue caveat); the in-memory transport delays in
    /// process. A time at or before now publishes immediately. For durable scheduling that
    /// survives broker loss, prefer the outbox overload
    /// (<c>IOutboxStore.Save(@event, enqueueAtUtc)</c>).
    /// </summary>
    /// <remarks>
    /// A default interface implementation throws <see cref="NotSupportedException"/> so
    /// custom bus implementations written against earlier versions keep compiling; the
    /// shipped bus overrides it.
    /// </remarks>
    Task PublishScheduled<TEvent>(TEvent @event, DateTimeOffset enqueueAtUtc, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
        => throw new NotSupportedException(
            $"{GetType().Name} does not implement {nameof(PublishScheduled)}. " +
            "Override it to support scheduled publishing.");
}
