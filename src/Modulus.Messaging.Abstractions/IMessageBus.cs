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
}
