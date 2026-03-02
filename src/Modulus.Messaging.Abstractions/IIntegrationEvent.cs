namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Represents an event that crosses module boundaries.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Gets the unique identifier of this event.</summary>
    Guid EventId { get; }

    /// <summary>Gets the UTC timestamp when this event occurred.</summary>
    DateTime OccurredOn { get; }

    /// <summary>Gets an optional correlation identifier for distributed tracing.</summary>
    string? CorrelationId { get; }
}
