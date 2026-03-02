using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Tests.Fixtures;

public record TestOrderCreatedEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string CustomerName { get; init; }
}
