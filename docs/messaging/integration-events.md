# Integration Events

Integration events represent something that happened in one module that other modules (or services) may need to react to. Unlike [domain events](/mediator/domain-events), which are dispatched in-process via `IMediator.Publish()`, integration events travel through a message broker and are delivered asynchronously.

## IIntegrationEvent Interface

Every integration event implements `IIntegrationEvent`:

```csharp
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
    string? CorrelationId { get; }
}
```

| Property | Type | Description |
|---|---|---|
| `EventId` | `Guid` | Unique identifier for this event instance. Used for deduplication in the inbox pattern. |
| `OccurredOn` | `DateTime` | Timestamp when the event was raised. |
| `CorrelationId` | `string?` | Optional correlation identifier for distributed tracing. |

## IntegrationEvent Base Record

The `IntegrationEvent` abstract record provides sensible defaults so you do not need to implement the interface properties manually:

```csharp
public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}
```

`EventId` and `OccurredOn` are auto-generated. `CorrelationId` defaults to `null` and can be set when needed.

## Defining Integration Events

Integration events are typically defined in a shared contracts project that both the publishing module and consuming modules reference. Use `sealed record` for immutability:

```csharp
public sealed record OrderCreatedEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderItemDto> Items) : IntegrationEvent;
```

```csharp
public sealed record PaymentProcessedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency) : IntegrationEvent;
```

```csharp
public sealed record InventoryReservedEvent(
    Guid OrderId,
    IReadOnlyList<ReservedItemDto> ReservedItems) : IntegrationEvent;
```

::: tip Use a shared contracts project
Define integration events in a lightweight project (e.g., `Modulus.Contracts` or `YourSolution.IntegrationEvents`) that both publishers and consumers reference. This keeps modules decoupled -- they share only the event contracts, not domain types.
:::

### Setting a Correlation ID

When you need distributed tracing across multiple events, set the `CorrelationId`:

```csharp
var @event = new OrderCreatedEvent(
    OrderId: order.Id,
    CustomerId: order.CustomerId,
    TotalAmount: order.Total,
    Items: order.Items.Select(i => new OrderItemDto(i.ProductId, i.Quantity)).ToList())
{
    CorrelationId = correlationId
};
```

## IIntegrationEventHandler Interface

Each handler implements `IIntegrationEventHandler<TEvent>`:

```csharp
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task Handle(TEvent @event, CancellationToken cancellationToken);
}
```

A single integration event can have multiple handlers. Each handler is invoked independently by MassTransit.

## Writing Handlers

Handlers contain the business logic that reacts to integration events. They are placed in the Application or Infrastructure layer of the consuming module:

### Example: Reserve Inventory When an Order Is Created

```csharp
public sealed class ReserveInventoryOnOrderCreatedHandler
    : IIntegrationEventHandler<OrderCreatedEvent>
{
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<ReserveInventoryOnOrderCreatedHandler> _logger;

    public ReserveInventoryOnOrderCreatedHandler(
        IInventoryService inventoryService,
        ILogger<ReserveInventoryOnOrderCreatedHandler> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
    }

    public async Task Handle(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Reserving inventory for order {OrderId}", @event.OrderId);

        foreach (var item in @event.Items)
        {
            await _inventoryService.ReserveAsync(
                item.ProductId,
                item.Quantity,
                cancellationToken);
        }
    }
}
```

### Example: Send Notification When Payment Is Processed

```csharp
public sealed class NotifyCustomerOnPaymentProcessedHandler
    : IIntegrationEventHandler<PaymentProcessedEvent>
{
    private readonly INotificationService _notificationService;

    public NotifyCustomerOnPaymentProcessedHandler(
        INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task Handle(
        PaymentProcessedEvent @event,
        CancellationToken cancellationToken)
    {
        await _notificationService.SendPaymentConfirmationAsync(
            @event.PaymentId,
            @event.OrderId,
            @event.Amount,
            cancellationToken);
    }
}
```

## Handler Auto-Discovery

When you call `AddModulusMessaging(options)`, the library scans the assemblies specified in `options.Assemblies` for all types that implement `IIntegrationEventHandler<TEvent>`. Each discovered handler is:

1. Wrapped with `IdempotentConsumerAdapter<TEvent>` (for inbox-based deduplication).
2. Registered as a MassTransit consumer.
3. Resolved from the DI container with a **scoped** lifetime.

::: info No manual registration needed
You do not need to register handlers individually or configure MassTransit consumers. Just ensure the assembly containing your handlers is included in `options.Assemblies`. The framework discovers and registers them automatically.
:::

## Domain Events vs Integration Events

Modulus makes a clear distinction between domain events and integration events. Understanding when to use each is important:

| Aspect | Domain Events | Integration Events |
|---|---|---|
| **Interface** | `IDomainEvent` | `IIntegrationEvent` |
| **Dispatch** | `IMediator.Publish()` | `IMessageBus.Publish()` |
| **Transport** | In-memory, same process | Message broker (InMemory, RabbitMQ, Azure Service Bus) |
| **Delivery** | Synchronous, awaited | Asynchronous, eventual consistency |
| **Scope** | Within a module or across modules in the same process | Cross-module, potentially cross-service |
| **Transaction** | Same transaction as the caller | Separate transaction in the consumer |
| **Failure model** | `AggregateException` thrown immediately | Retry policies, dead-letter queues, outbox/inbox |
| **Handler** | `IDomainEventHandler<T>` | `IIntegrationEventHandler<T>` |

### When to Use Domain Events

- Reacting to state changes within the same bounded context
- Side effects that must happen in the same transaction
- In-process workflows where immediate consistency is required

### When to Use Integration Events

- Communicating state changes across module boundaries
- Scenarios where loose coupling between modules is essential
- When you need reliable delivery with retries, dead-letter queues, and outbox guarantees
- Cross-service communication in a distributed system

::: tip A common pattern
A domain event handler publishes a corresponding integration event. This lets you keep in-process logic synchronous while still notifying external modules asynchronously:

```csharp
public sealed class PublishOrderCreatedIntegrationEventHandler
    : IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IMessageBus _messageBus;

    public PublishOrderCreatedIntegrationEventHandler(IMessageBus messageBus)
    {
        _messageBus = messageBus;
    }

    public async Task Handle(
        OrderPlacedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        await _messageBus.Publish(
            new OrderCreatedEvent(
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.Total,
                domainEvent.Items),
            cancellationToken);
    }
}
```
:::

## Best Practices

- **Keep events small.** Include only the data consumers need. Avoid embedding entire aggregate state into an event.
- **Use records for immutability.** Integration events should be immutable. Use `sealed record` to prevent inheritance and mutation.
- **Version events carefully.** Adding optional properties is safe. Removing or renaming properties is a breaking change for consumers.
- **Name events in past tense.** Events represent something that already happened: `OrderCreated`, `PaymentProcessed`, `InventoryReserved`.
- **Define events in shared contracts.** Keep event definitions in a lightweight shared project, not in the publishing module's domain layer.

## See Also

- [Message Bus](./message-bus) -- The `IMessageBus` API for publishing and sending
- [Transports](./transports) -- Configure the message broker
- [Outbox Pattern](./outbox-pattern) -- Reliable event publishing with transactional outbox
- [Inbox Pattern](./inbox-pattern) -- Idempotent event consumption
- [Domain Events](/mediator/domain-events) -- In-process domain events via the mediator
