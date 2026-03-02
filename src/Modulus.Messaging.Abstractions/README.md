# Modulus.Messaging.Abstractions

Abstractions for the Modulus messaging system — `IMessageBus`, `IIntegrationEvent`, and outbox pattern interfaces.

## Installation

```bash
dotnet add package Modulus.Messaging.Abstractions
```

## Key Types

### Integration Events

```csharp
// Define an integration event
public record OrderShipped(Guid OrderId, DateTime ShippedAt)
    : IntegrationEvent;

// IntegrationEvent base class auto-generates:
//   EventId      = Guid.NewGuid()
//   OccurredOn   = DateTime.UtcNow
//   CorrelationId = null (optional, settable via init)
```

### Message Bus

```csharp
public interface IMessageBus
{
    // Publish an event to all subscribers
    Task Publish<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IIntegrationEvent;

    // Send a command (routed by type name)
    Task Send<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : class;

    // Send a command to a specific destination
    Task Send<TCommand>(TCommand command, Uri destination, CancellationToken ct = default)
        where TCommand : class;
}
```

### Integration Event Handlers

```csharp
public class OrderShippedHandler : IIntegrationEventHandler<OrderShipped>
{
    public Task Handle(OrderShipped @event, CancellationToken ct)
    {
        // React to the cross-module event
        return Task.CompletedTask;
    }
}
```

### Outbox Pattern

The `IOutboxStore` interface enables the transactional outbox pattern — events are stored alongside your business data in the same transaction, then published reliably by a background processor.

```csharp
public interface IOutboxStore
{
    Task Save(IIntegrationEvent @event, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, CancellationToken ct = default);
    Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken ct = default);
}
```

## Learn More

See the [Modulus repository](https://github.com/adamwyatt34/Modulus) for full documentation.
