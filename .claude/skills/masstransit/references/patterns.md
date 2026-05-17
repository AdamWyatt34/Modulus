# MassTransit Patterns Reference

## Contents
- Integration Event Definition
- Handler Implementation
- Outbox Store Usage
- Inbox / Idempotency
- Anti-Patterns

---

## Integration Event Definition

Events live in the publishing module's **Integration** project. Always extend `IntegrationEvent` (the abstract record), never implement `IIntegrationEvent` directly — the base record provides `EventId`, `OccurredOn`, and `CorrelationId` automatically.

```csharp
// src/Modules/Orders/Integration/OrderPlacedEvent.cs
namespace Orders.Integration;

public record OrderPlacedEvent(Guid OrderId, string CustomerId) : IntegrationEvent;
```

`IntegrationEvent` auto-assigns `EventId = Guid.NewGuid()` and `OccurredOn = DateTime.UtcNow` on construction. Override only when replaying events in tests.

---

## Handler Implementation

```csharp
// src/Modules/Notifications/Application/OrderPlacedHandler.cs
namespace Notifications.Application;

public sealed class OrderPlacedHandler(INotificationService notifications)
    : IIntegrationEventHandler<OrderPlacedEvent>
{
    public async Task Handle(OrderPlacedEvent @event, CancellationToken ct)
    {
        await notifications.SendOrderConfirmationAsync(@event.CustomerId, @event.OrderId, ct);
    }
}
```

- **Always `sealed`** — these are DI-leaf classes, inheritance adds no value
- **Primary constructor** for injected dependencies
- **Never return a value** — the interface is `Task Handle(...)`, not `Task<Result> Handle(...)`
- The source generator discovers all `IIntegrationEventHandler<>` implementations automatically — no manual registration needed

---

## Outbox Store Usage

`IOutboxStore.Save` persists the event JSON to the `OutboxMessages` table. Call it within the **same unit of work** as your domain write, then commit once:

```csharp
public sealed class PlaceOrderHandler(
    IOrderRepository repo,
    IOutboxStore outbox) : ICommandHandler<PlaceOrderCommand>
{
    public async Task<Result> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);
        await repo.AddAsync(order, ct);
        await outbox.Save(new OrderPlacedEvent(order.Id, cmd.CustomerId), ct);
        await repo.SaveChangesAsync(ct);   // single commit — both writes atomic
        return Result.Success();
    }
}
```

`OutboxProcessor` (`BackgroundService`) polls every `OutboxPollInterval` (default 5s), fetches up to `OutboxBatchSize` (default 100) pending messages, publishes via MassTransit, then marks them processed.

**Event type is stored as `AssemblyQualifiedName`** (`Type.AssemblyQualifiedName`). If you rename an event class and have unprocessed rows in the outbox, the processor will log a warning and skip them — clear stale rows manually before renaming.

---

## Inbox / Idempotency

The `IdempotentConsumerAdapter<TEvent>` wraps every MassTransit consumer. It tracks delivery using `EventId + HandlerTypeName`. If `IInboxStore` is not registered, the adapter falls through to direct handler execution (no deduplication).

```csharp
// IInboxStore contract
public interface IInboxStore
{
    Task Save(IIntegrationEvent @event, CancellationToken ct);
    Task<bool> HasBeenProcessed(Guid eventId, string handlerName, CancellationToken ct);
    Task RecordConsumer(Guid eventId, string handlerName, CancellationToken ct);
}
```

Deduplication is **per-handler**, not per-event. The same event can be processed by multiple handlers — each is independently tracked. This is correct: if handler A succeeds and handler B crashes, only B retries.

---

## Anti-Patterns

### WARNING: Publishing via IBus directly

**The Problem:**

```csharp
// BAD — bypasses outbox, no at-least-once guarantee
public sealed class BadHandler(IBus bus) : ICommandHandler<PlaceOrderCommand>
{
    public async Task<Result> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        await bus.Publish(new OrderPlacedEvent(cmd.OrderId, cmd.CustomerId), ct);
        return Result.Success();
    }
}
```

**Why This Breaks:**
1. If the app crashes after `Publish` but before the DB commit, the event is lost forever
2. If the broker is unavailable, `Publish` throws and the command fails — no retry
3. Consumers are NOT protected by the inbox — duplicates can occur on broker redelivery

**The Fix:**

```csharp
// GOOD — save to outbox within the same transaction
await _outboxStore.Save(new OrderPlacedEvent(cmd.OrderId, cmd.CustomerId), ct);
await _repo.SaveChangesAsync(ct);
```

---

### WARNING: Using domain events for cross-module communication

**The Problem:**

```csharp
// BAD — domain events are in-process only
_mediator.Publish(new OrderPlacedDomainEvent(order.Id));
// Handler in a different module will never receive this across process boundaries
```

**Why This Breaks:**
1. Domain events are dispatched synchronously in-process — they do not cross module process boundaries
2. No persistence, no retry, no deduplication
3. Silently fails across service boundaries with no error

**The Fix:** Use `IOutboxStore.Save(new OrderPlacedEvent(...))` with an `IIntegrationEventHandler<>` in the subscribing module.

---

### WARNING: Handlers that throw exceptions

**The Problem:**

```csharp
// BAD — throws on failure, MassTransit retries the whole consumer
public Task Handle(OrderPlacedEvent @event, CancellationToken ct)
{
    if (!_repo.Exists(@event.OrderId))
        throw new InvalidOperationException("Order not found");
    return Task.CompletedTask;
}
```

**Why This Breaks:**
1. MassTransit retries the message on exception — with inbox deduplication, the retry will be a no-op after the first success, causing silent data loss
2. Unhandled exceptions surface as broker poison messages after retry exhaustion
3. The inbox records the consumer **after** `Handle()` returns — a thrown exception means the record is never written, so retries re-execute the handler

**The Fix:** Log and swallow expected errors, or use a dead-letter queue pattern. The handler contract is `Task` — there is no `Result` to return.

---

## Related Skills

- See the **csharp** skill for sealed classes, primary constructors, and record patterns
- See the **xunit** skill for messaging integration test setup with EF Core InMemory
