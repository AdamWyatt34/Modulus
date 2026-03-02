# Domain Events

Domain events represent something meaningful that happened within a module's domain. They are published in-process via `IMediator.Publish()` and handled by one or more `IDomainEventHandler<TEvent>` implementations within the same application boundary.

## IDomainEvent Interface

Every domain event implements `IDomainEvent`:

```csharp
public interface IDomainEvent
{
    Guid Id { get; }
    DateTime OccurredOnUtc { get; }
}
```

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Unique identifier for this event instance |
| `OccurredOnUtc` | `DateTime` | UTC timestamp when the event was raised |

## Defining Domain Events

Domain events are typically defined in the Domain layer of a module. Use records for immutability and value equality:

```csharp
public sealed record OrderPlacedEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal Total) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
```

```csharp
public sealed record ProductCreatedEvent(
    Guid ProductId,
    string Name,
    string Sku) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
```

```csharp
public sealed record OrderStatusChangedEvent(
    Guid OrderId,
    OrderStatus OldStatus,
    OrderStatus NewStatus) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
```

::: tip Use a base record for convenience
If you find yourself repeating the `Id` and `OccurredOnUtc` boilerplate, consider creating a base record in your SharedKernel:

```csharp
public abstract record DomainEventBase : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

// Then your events become:
public sealed record OrderPlacedEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal Total) : DomainEventBase;
```
:::

## IDomainEventHandler Interface

Each handler implements `IDomainEventHandler<TEvent>`:

```csharp
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}
```

## Handling Domain Events

Handlers are placed in the Application layer and are auto-discovered by Scrutor when you call `AddModulusMediator(assemblies)`.

### Example: Send a Notification When an Order Is Placed

```csharp
public sealed class SendOrderConfirmationHandler
    : IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IEmailService _emailService;
    private readonly ICustomerRepository _customerRepository;

    public SendOrderConfirmationHandler(
        IEmailService emailService,
        ICustomerRepository customerRepository)
    {
        _emailService = emailService;
        _customerRepository = customerRepository;
    }

    public async Task Handle(
        OrderPlacedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(
            domainEvent.CustomerId, cancellationToken);

        if (customer is null) return;

        await _emailService.SendOrderConfirmationAsync(
            customer.Email,
            domainEvent.OrderId,
            domainEvent.Total,
            cancellationToken);
    }
}
```

### Example: Update Inventory When an Order Is Placed

```csharp
public sealed class ReserveInventoryHandler
    : IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IInventoryService _inventoryService;

    public ReserveInventoryHandler(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    public async Task Handle(
        OrderPlacedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        await _inventoryService.ReserveStockAsync(
            domainEvent.OrderId, cancellationToken);
    }
}
```

Both handlers above will be invoked when `OrderPlacedEvent` is published. A single domain event can have any number of handlers.

## Publishing Domain Events

Use `IMediator.Publish()` to dispatch a domain event to all registered handlers:

```csharp
public sealed class PlaceOrderCommandHandler
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMediator _mediator;

    public PlaceOrderCommandHandler(
        IOrderRepository orderRepository,
        IMediator mediator)
    {
        _orderRepository = orderRepository;
        _mediator = mediator;
    }

    public async Task<Result<Guid>> Handle(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        var order = new Order(command.CustomerId, command.Items);

        await _orderRepository.AddAsync(order, cancellationToken);

        // Publish domain event -- all handlers will be invoked
        await _mediator.Publish(
            new OrderPlacedEvent(order.Id, command.CustomerId, order.Total),
            cancellationToken);

        return order.Id;
    }
}
```

### Raising Events from Aggregate Roots

A common DDD pattern is to collect domain events on the aggregate root and publish them after persistence:

```csharp
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
```

```csharp
public sealed class Order : AggregateRoot
{
    public void Place()
    {
        Status = OrderStatus.Placed;

        RaiseDomainEvent(new OrderPlacedEvent(Id, CustomerId, Total));
    }
}
```

Then publish collected events after saving:

```csharp
public async Task<Result<Guid>> Handle(
    PlaceOrderCommand command,
    CancellationToken cancellationToken)
{
    var order = new Order(command.CustomerId, command.Items);
    order.Place();

    await _orderRepository.AddAsync(order, cancellationToken);
    await _unitOfWork.SaveChangesAsync(cancellationToken);

    // Publish all collected domain events
    foreach (var domainEvent in order.DomainEvents)
    {
        await _mediator.Publish(domainEvent, cancellationToken);
    }

    order.ClearDomainEvents();

    return order.Id;
}
```

## Error Handling Semantics

When `mediator.Publish()` is called:

1. All registered handlers for the event type are resolved from the DI container.
2. All handlers are invoked.
3. If one or more handlers throw an exception, the exceptions are collected and thrown as an `AggregateException` after all handlers have been given a chance to run.

::: warning Handlers do not short-circuit each other
Unlike commands and queries, if one domain event handler fails, the remaining handlers still execute. All exceptions are aggregated. This ensures that a failure in one handler does not silently prevent other handlers from running.
:::

## Domain Events vs Integration Events

Modulus distinguishes between domain events and integration events:

| Aspect | Domain Events | Integration Events |
|---|---|---|
| **Scope** | In-process, within a single module or across modules in the same process | Cross-module, potentially cross-service via a message bus |
| **Transport** | `IMediator.Publish()` -- direct in-memory dispatch | MassTransit message bus (InMemory, RabbitMQ, Azure Service Bus) |
| **Delivery** | Synchronous (awaited), same transaction scope | Asynchronous, eventual consistency |
| **Contract** | `IDomainEvent` | `IIntegrationEvent` |
| **Coupling** | Handlers reference domain types directly | Handlers reference shared integration contracts |
| **Failure model** | `AggregateException` thrown immediately | Retry policies, dead-letter queues, outbox pattern |

**When to use domain events:**
- Reacting to something that happened within the same bounded context
- Side effects that should happen in the same transaction (e.g., updating a read model)
- In-process event-driven workflows

**When to use integration events:**
- Communicating across module boundaries where loose coupling is required
- Communicating across services in a distributed system
- When you need reliable delivery with retries and outbox guarantees

::: info Learn more about integration events
See the [Messaging documentation](/messaging/) for details on integration events, transports, and the outbox pattern.
:::

## Pipeline Behaviors and Domain Events

Domain events are **not** wrapped by pipeline behaviors. When you call `mediator.Publish()`, the event handlers are invoked directly -- no `ValidationBehavior`, `LoggingBehavior`, or other behaviors are applied.

If you need cross-cutting concerns for domain event handling, implement them directly in your handlers or use a decorator pattern.

## See Also

- [Commands & Queries](./commands-queries) -- The CQRS request types
- [Pipeline Behaviors](./pipeline-behaviors) -- Pipeline middleware (does not apply to domain events)
- [Messaging: Integration Events](/messaging/integration-events) -- Cross-module event communication
