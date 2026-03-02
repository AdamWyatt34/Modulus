namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Handles a domain event.
/// </summary>
/// <typeparam name="TEvent">The type of domain event to handle.</typeparam>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles the domain event.</summary>
    /// <param name="domainEvent">The domain event to handle.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken = default);
}
