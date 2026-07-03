# Message Bus

The `IMessageBus` interface is the single entry point for publishing integration events through the messaging infrastructure. It abstracts away the underlying transport (InMemory, RabbitMQ, or Azure Service Bus), giving you a clean, transport-agnostic interface.

## IMessageBus Interface

<!-- verify -->
```csharp
public interface IMessageBus
{
    Task Publish<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}
```

`Publish<TEvent>()` publishes an integration event to **all** subscribers (fan-out). Events are the only cross-module messaging primitive in Modulus: something happened, and zero or more other modules react to it.

::: info Where did `Send` go?
Earlier versions exposed point-to-point `Send<TCommand>()` overloads. They were removed in 2.0: Modulus never ran a consuming pipeline for commands (the receiving side had to consume the queue itself), so the API implied a feature that didn't exist. In-process commands go through the mediator; cross-module communication goes through integration events. If a designed point-to-point story lands later, it will come back with real consumer support.
:::

## Publish -- Fan-Out to All Subscribers

`Publish<TEvent>()` delivers an integration event to every module or service that has a registered handler for that event type. If no subscribers exist, the event is silently discarded.

### Example: Publish an Order Created Event

```csharp
public sealed class PlaceOrderCommandHandler
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMessageBus _messageBus;

    public PlaceOrderCommandHandler(
        IOrderRepository orderRepository,
        IMessageBus messageBus)
    {
        _orderRepository = orderRepository;
        _messageBus = messageBus;
    }

    public async Task<Result<Guid>> Handle(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        var order = new Order(command.CustomerId, command.Items);
        await _orderRepository.AddAsync(order, cancellationToken);

        // Publish to all subscribers -- inventory, notifications, analytics, etc.
        await _messageBus.Publish(
            new OrderCreatedEvent(
                order.Id,
                command.CustomerId,
                order.Total,
                order.Items.Select(i => new OrderItemDto(i.ProductId, i.Quantity)).ToList()),
            cancellationToken);

        return order.Id;
    }
}
```

Multiple handlers across different modules can react to the same event independently:

```csharp
// In the Inventory module
public sealed class ReserveInventoryHandler
    : IIntegrationEventHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent @event, CancellationToken ct)
    {
        // Reserve stock for each item in the order
    }
}

// In the Notifications module
public sealed class SendOrderConfirmationHandler
    : IIntegrationEventHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent @event, CancellationToken ct)
    {
        // Send confirmation email to the customer
    }
}

// In the Analytics module
public sealed class TrackOrderMetricsHandler
    : IIntegrationEventHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent @event, CancellationToken ct)
    {
        // Record order metrics for dashboards
    }
}
```

## Using IMessageBus in Endpoints

Inject `IMessageBus` from the DI container and use it in your API endpoints:

```csharp
public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders");

        group.MapPost("/", async (
            PlaceOrderRequest request,
            IMediator mediator,
            IMessageBus messageBus,
            CancellationToken ct) =>
        {
            // 1. Handle the command via the mediator
            var result = await mediator.Send(
                new PlaceOrderCommand(request.CustomerId, request.Items), ct);

            // 2. Publish an integration event for other modules
            if (result.IsSuccess)
            {
                await messageBus.Publish(
                    new OrderCreatedEvent(
                        result.Value,
                        request.CustomerId,
                        request.TotalAmount,
                        request.Items),
                    ct);
            }

            return result.Match(
                onSuccess: id => Results.Created($"/orders/{id}", id),
                onFailure: errors => Results.BadRequest(errors));
        });
    }
}
```

::: tip Prefer publishing from command handlers
The example above publishes from the endpoint for clarity. In practice, publish integration events from within your command handlers or domain event handlers, so the publish logic is co-located with the business logic and participates in the same unit of work.
:::

## Error Handling

When `Publish` fails (e.g., the broker is unreachable), the exception propagates to the caller. To prevent message loss in failure scenarios, use the [Outbox Pattern](./outbox-pattern) to save messages to your database first and let a background processor publish them reliably.

## See Also

- [Integration Events](./integration-events) -- Define events and handlers
- [Transports](./transports) -- Configure the underlying message broker
- [Outbox Pattern](./outbox-pattern) -- Reliable message publishing
- [Inbox Pattern](./inbox-pattern) -- Idempotent message consumption
