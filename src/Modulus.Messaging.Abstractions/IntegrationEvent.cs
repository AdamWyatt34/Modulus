namespace Modulus.Messaging.Abstractions;

/// <summary>
/// Base class for integration events with auto-generated defaults.
/// </summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;

    /// <inheritdoc />
    public string? CorrelationId { get; init; }
}
