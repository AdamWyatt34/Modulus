using Modulus.Mediator.Abstractions;

namespace SampleApp.Orders.Application.Queries.GetOrder;

public sealed record GetOrder(Guid OrderId) : IQuery<OrderDto>;
