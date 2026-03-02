namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents a domain event for in-process pub/sub within a module boundary.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique identifier for this event instance, useful for idempotency checks.</summary>
    Guid Id { get; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    DateTime OccurredOnUtc { get; }
}
