using Modulus.Mediator.Abstractions;
using Modulus.Messaging.Abstractions;
using SampleApp.Orders.Domain.Entities;
using SampleApp.Orders.Domain.Repositories;
using SampleApp.Orders.Integration.IntegrationEvents;

namespace SampleApp.Orders.Application.Commands.CreateOrder;

/// <summary>
/// Creates an order and records an <see cref="OrderPlaced"/> integration event in the
/// transactional outbox. The order itself is persisted by the mediator's UnitOfWorkBehavior
/// after this handler returns success; the OutboxProcessor background service later publishes
/// the event to the configured transport, where the Notifications module consumes it.
/// </summary>
public sealed class CreateOrderHandler(
    IOrderRepository orders,
    IOutboxStore outbox) : ICommandHandler<CreateOrder, Guid>
{
    public async Task<Result<Guid>> Handle(CreateOrder command, CancellationToken cancellationToken = default)
    {
        var order = Order.Create(Guid.NewGuid(), command.CustomerName, command.Total);

        await orders.AddAsync(order, cancellationToken);
        await outbox.Save(new OrderPlaced(order.Id, order.Total), cancellationToken);

        return order.Id;
    }
}
