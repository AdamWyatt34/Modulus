using Modulus.Mediator.Abstractions;

namespace SampleApp.Orders.Application.Commands.CreateOrder;

public sealed record CreateOrder(string CustomerName, decimal Total) : ICommand<Guid>;
