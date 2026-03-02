# Message Bus

The `IMessageBus` interface is the single entry point for publishing integration events and sending commands through the messaging infrastructure. It abstracts away MassTransit's API, giving you a clean, transport-agnostic interface.

## IMessageBus Interface

```csharp
public interface IMessageBus
{
    Task Publish<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;

    Task Send<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : class;

    Task Send<TCommand>(
        TCommand command,
        Uri destination,
        CancellationToken cancellationToken = default)
        where TCommand : class;
}
```

| Method | Description |
|---|---|
| `Publish<TEvent>()` | Publishes an integration event to **all** subscribers (fan-out). |
| `Send<TCommand>()` | Sends a command to a **single** consumer, routed by convention. |
| `Send<TCommand>(command, destination)` | Sends a command to a **specific** endpoint URI. |

## Publish vs Send

Understanding the difference between publishing and sending is critical for designing correct message flows:

| Aspect | Publish | Send |
|---|---|---|
| **Semantics** | Fan-out to all subscribers | Point-to-point to a single consumer |
| **Recipients** | Zero or more | Exactly one |
| **Use case** | Integration events -- notify others that something happened | Commands -- tell a specific service to do something |
| **Constraint** | `TEvent : IIntegrationEvent` | `TCommand : class` |
| **Routing** | Topic/exchange-based | Queue-based |

::: warning Choose the right method
Use `Publish` for events (notifications). Use `Send` for commands (instructions). Publishing a command or sending an event is a design smell -- it conflates notification semantics with instruction semantics.
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

## Send -- Point-to-Point Commands

`Send<TCommand>()` delivers a command to a single consumer. By convention, the command is routed to a queue named `queue:{TypeName}`.

### Example: Send a Processing Command

```csharp
public sealed class ProcessPaymentCommandHandler
    : IIntegrationEventHandler<OrderCreatedEvent>
{
    private readonly IMessageBus _messageBus;

    public ProcessPaymentCommandHandler(IMessageBus messageBus)
    {
        _messageBus = messageBus;
    }

    public async Task Handle(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken)
    {
        // Send a command to the payment service -- exactly one consumer will handle it
        await _messageBus.Send(
            new ChargeCustomerCommand(
                @event.OrderId,
                @event.CustomerId,
                @event.TotalAmount),
            cancellationToken);
    }
}
```

::: info Routing convention
When you call `Send<TCommand>(command)` without a destination URI, MassTransit routes the command to a queue named after the command type. For example, `ChargeCustomerCommand` routes to `queue:ChargeCustomerCommand`.
:::

## Send with Explicit Destination

`Send<TCommand>(command, destination)` lets you specify the exact endpoint URI where the command should be delivered. This is useful when the destination does not follow the default naming convention or when you need to target a specific service instance.

### Example: Send to a Specific Queue

```csharp
// Send to a specific RabbitMQ queue
await _messageBus.Send(
    new ChargeCustomerCommand(orderId, customerId, amount),
    new Uri("queue:payment-processing"),
    cancellationToken);
```

### Example: Send to an Azure Service Bus Queue

```csharp
// Send to a specific Azure Service Bus queue
await _messageBus.Send(
    new GenerateInvoiceCommand(orderId, customerId),
    new Uri("queue:invoice-generation"),
    cancellationToken);
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

When `Publish` or `Send` fails (e.g., the broker is unreachable), the exception propagates to the caller. To prevent message loss in failure scenarios, use the [Outbox Pattern](./outbox-pattern) to save messages to your database first and let a background processor publish them reliably.

## See Also

- [Integration Events](./integration-events) -- Define events and handlers
- [Transports](./transports) -- Configure the underlying message broker
- [Outbox Pattern](./outbox-pattern) -- Reliable message publishing
- [Inbox Pattern](./inbox-pattern) -- Idempotent message consumption
