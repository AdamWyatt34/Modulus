namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Handles an integration event that crosses module boundaries.
/// </summary>
/// <typeparam name="TEvent">The type of integration event to handle.</typeparam>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>Handles the integration event.</summary>
    /// <param name="event">The integration event to handle.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Handle(TEvent @event, CancellationToken cancellationToken = default);
}
