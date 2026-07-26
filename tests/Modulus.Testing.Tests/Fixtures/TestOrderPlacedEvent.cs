using Modulus.Messaging.Abstractions;

namespace Modulus.Testing.Tests.Fixtures;

public sealed record TestOrderPlacedEvent : IntegrationEvent
{
    public required int OrderId { get; init; }

    public required string CustomerName { get; init; }
}
