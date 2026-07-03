using Modulus.Messaging.Abstractions;

namespace SampleApp.Orders.Integration.IntegrationEvents;

public sealed record OrderPlaced(Guid OrderId, decimal Total) : IntegrationEvent;
