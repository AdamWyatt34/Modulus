namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Publishes integration events and sends commands across module boundaries.
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
    /// Sends a command to its handler.
    /// </summary>
    /// <typeparam name="TCommand">The type of command.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class;

    /// <summary>
    /// Sends a command to a specific destination.
    /// </summary>
    /// <typeparam name="TCommand">The type of command.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="destination">The destination URI for the command.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Send<TCommand>(TCommand command, Uri destination, CancellationToken cancellationToken = default)
        where TCommand : class;
}
