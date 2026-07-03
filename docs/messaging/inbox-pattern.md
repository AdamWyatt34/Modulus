# Inbox Pattern

The inbox pattern ensures **idempotent message consumption** -- even if the same message is delivered more than once, your handlers process it only once. Modulus provides a built-in inbox implementation that the consumer pipeline applies to every `IIntegrationEventHandler<T>` automatically.

## The Problem

Message brokers guarantee **at-least-once delivery**, not exactly-once. Duplicate messages occur in several scenarios:

- **Network retries:** The broker delivers a message, but the consumer's acknowledgment is lost. The broker re-delivers.
- **Outbox re-publishing:** The [OutboxProcessor](./outbox-pattern) published a message but crashed before marking it as processed. On restart, it publishes the same message again.
- **Broker failover:** During broker cluster failover, messages in flight may be re-queued.
- **Consumer timeout:** The consumer takes too long to process a message. The broker assumes it failed and re-delivers.

Without deduplication, a handler that processes a payment, sends an email, or updates inventory could execute these side effects multiple times.

## How It Works

Modulus solves this in the `ConsumerDispatcher`, the transport-agnostic consumer pipeline that dispatches every delivered message. Before invoking each handler, it checks an inbox store to determine whether that handler has already processed the message.

```mermaid
sequenceDiagram
    participant Bus as Transport
    participant Dispatcher as ConsumerDispatcher
    participant Inbox as IInboxStore
    participant Handler as IIntegrationEventHandler

    Bus->>Dispatcher: Deliver message
    Dispatcher->>Inbox: Save(event)
    loop Every registered handler
        Dispatcher->>Inbox: HasBeenProcessed(eventId, handlerName)?
        alt Already processed
            Inbox-->>Dispatcher: true
            Dispatcher->>Dispatcher: Skip handler
        else Not yet processed
            Inbox-->>Dispatcher: false
            Dispatcher->>Handler: Handle(event)
            Handler-->>Dispatcher: Completed
            Dispatcher->>Inbox: RecordConsumer(eventId, handlerName)
        end
    end
    Dispatcher-->>Bus: Acknowledge
```

### Processing Flow

The `ConsumerDispatcher` follows this logic for each incoming message:

1. **No inbox registered:** If no `IInboxStore` is registered in the DI container, the dispatcher falls through to direct handler execution. The inbox is entirely optional.
2. **Inbox registered:**
   1. **Save the event** to the inbox store (records that this message arrived; the save itself is idempotent).
   2. For **each registered handler**, check `HasBeenProcessed(eventId, handlerName)` -- has this specific handler already processed this specific event?
   3. If **already processed** -- skip that handler.
   4. If **not yet processed** -- invoke the handler, then call `RecordConsumer(eventId, handlerName)` to mark it as processed.
3. The message is acknowledged only after all handlers succeed. If any handler throws, the dispatcher retries in-process per `ConsumerRetry`; on redelivery, only the handlers that have not recorded success re-run.

::: info Per-handler deduplication
The inbox tracks processing at the `(eventId, handlerName)` level. If an event has three handlers, each handler is independently tracked. Handler A being marked as processed does not affect whether Handler B or Handler C runs.
:::

## IInboxStore Interface

The `IInboxStore` interface defines the contract for inbox persistence:

<!-- verify -->
```csharp
public interface IInboxStore
{
    Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InboxMessage>> GetPending(int batchSize, CancellationToken cancellationToken = default);

    Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<bool> HasBeenProcessed(Guid messageId, string handlerName, CancellationToken cancellationToken = default);

    Task RecordConsumer(Guid messageId, string handlerName, CancellationToken cancellationToken = default);
}
```

| Method | Description |
|---|---|
| `Save` | Persists the incoming event as an `InboxMessage`. |
| `GetPending` | Retrieves unprocessed inbox messages (used for reprocessing scenarios). |
| `MarkAsProcessed` | Marks inbox messages as fully processed. |
| `HasBeenProcessed` | Checks if a specific handler has already processed a specific message. |
| `RecordConsumer` | Records that a specific handler has processed a specific message. |

## InboxMessage Model

Each incoming event is stored as an `InboxMessage`:

<!-- verify -->
```csharp
public sealed class InboxMessage
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Content { get; init; }
    public required DateTime OccurredOnUtc { get; init; }
    public DateTime? ProcessedOnUtc { get; set; }
}
```

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | The `EventId` from the integration event. Used as the deduplication key. |
| `Type` | `string` | Assembly-qualified type name of the event. |
| `Content` | `string` | JSON-serialized event payload. |
| `OccurredOnUtc` | `DateTime` | When the original event was raised. |
| `ProcessedOnUtc` | `DateTime?` | When all handlers for this message completed. `null` while pending. |

## InboxMessageConsumer Model

Per-handler tracking is stored in the `InboxMessageConsumer` table with a composite primary key:

<!-- verify -->
```csharp
public sealed class InboxMessageConsumer
{
    public required Guid InboxMessageId { get; init; }
    public required string Name { get; init; }
}
```

| Property | Type | Description |
|---|---|---|
| `InboxMessageId` | `Guid` | Foreign key to the `InboxMessage`. |
| `Name` | `string` | The fully qualified name of the handler type. |

The composite key `(InboxMessageId, Name)` ensures that each handler is recorded exactly once per message. If two threads attempt to record the same consumer simultaneously, the database constraint prevents duplicates.

## ConsumerDispatcher

The `ConsumerDispatcher` is the consumer pipeline that every transport hands delivered messages to. It resolves the event type, deserializes the body, and invokes **all** registered `IIntegrationEventHandler<TEvent>` implementations inside a DI scope, each wrapped with the inbox check.

**You do not need to create or register anything.** The pipeline is wired by `AddModulusMessaging`.

### Behavior Summary

```csharp
// Simplified pseudocode of the ConsumerDispatcher's per-message handling
var handlers = ResolveHandlers(eventType);

// No inbox? Fall through to direct execution
if (inboxStore is null)
{
    foreach (var handler in handlers)
        await handler.Handle(@event, cancellationToken);
    return;
}

// Save the event to the inbox (idempotent)
await inboxStore.Save(@event, cancellationToken);

foreach (var handler in handlers)
{
    // Skip handlers that already processed this event
    if (await inboxStore.HasBeenProcessed(@event.EventId, handler.Name, cancellationToken))
        continue;

    await handler.Handle(@event, cancellationToken);

    // Record that this handler has processed this event
    await inboxStore.RecordConsumer(@event.EventId, handler.Name, cancellationToken);
}
```

::: tip The inbox is optional
If you do not register an `IInboxStore` in the DI container, the dispatcher delegates to the handlers directly. No deduplication occurs. This lets you opt in to the inbox pattern only when you need it.
:::

## EfInboxStore

The `EfInboxStore` is the built-in Entity Framework Core implementation of `IInboxStore`. It handles concurrent insert race conditions gracefully -- if two threads attempt to save the same inbox message simultaneously, the second insert is caught and ignored rather than throwing an exception.

### Handling Concurrent Inserts

When the same message arrives on two threads simultaneously:

1. Thread A calls `Save(@event)` -- inserts successfully.
2. Thread B calls `Save(@event)` -- catches the duplicate key violation and ignores it.
3. Both threads call `HasBeenProcessed` -- only one returns `false` first.
4. The winning thread processes the event and calls `RecordConsumer`.
5. The losing thread sees `HasBeenProcessed` return `true` and skips.

This race-safe behavior ensures correctness without requiring distributed locks.

## Usage Example

To enable the inbox pattern, register the inbox database context and store:

<!-- verify -->
```csharp
// Registers InboxDbContext and the EF Core inbox store
builder.Services.AddModulusInbox(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Database")));
```

That is all. The `ConsumerDispatcher` detects the registered `IInboxStore` and activates deduplication for all handlers automatically.

Your handler code does not change at all:

```csharp
public sealed class ReserveInventoryHandler
    : IIntegrationEventHandler<OrderCreatedEvent>
{
    private readonly IInventoryService _inventoryService;

    public ReserveInventoryHandler(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    public async Task Handle(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken)
    {
        // This handler will only execute once per event, even if
        // the message is delivered multiple times by the broker.
        await _inventoryService.ReserveAsync(
            @event.OrderId,
            @event.Items,
            cancellationToken);
    }
}
```

::: warning Ensure the inbox tables exist
The `InboxMessage` and `InboxMessageConsumer` tables live in the `InboxDbContext` that ships with the package. Ensure the corresponding tables exist in your database via EF Core migrations (`app.UseModulusMessagingMigrationsAsync()` applies them at startup).
:::

## Outbox + Inbox: End-to-End Reliability

The outbox and inbox patterns complement each other to provide reliable end-to-end message delivery:

```mermaid
flowchart LR
    A[Command Handler] -->|Same TX| B[(Database)]
    A -->|Same TX| C[Outbox Store]
    C -->|Poll & Publish| D[OutboxProcessor]
    D -->|Publish| E[Broker]
    E -->|Deliver| F[ConsumerDispatcher]
    F -->|Check| G[Inbox Store]
    G -->|Not processed| H[Handler]
    G -->|Already processed| I[Skip]
```

| Layer | Pattern | Guarantee |
|---|---|---|
| **Publisher side** | Outbox | Messages are persisted atomically with domain changes and published at least once. |
| **Consumer side** | Inbox | Messages are processed exactly once per handler, regardless of how many times they are delivered. |

Together, they provide **effectively exactly-once processing**:
- The outbox ensures no messages are lost.
- The inbox ensures no messages are processed more than once.

## Best Practices

- **Always pair outbox with inbox.** The outbox guarantees at-least-once publishing, which means duplicates will occur. The inbox ensures each handler processes each message only once.
- **Include inbox tables in your migrations.** The `InboxMessage` and `InboxMessageConsumer` tables must exist in your database. Run EF Core migrations to create them.
- **Monitor the inbox table.** Like the outbox, the inbox table grows over time. Implement a cleanup job to remove old processed entries.
- **Keep handlers idempotent where possible.** Even with the inbox pattern, designing naturally idempotent handlers (e.g., upserts instead of inserts) adds an extra layer of safety.
- **Do not rely solely on the inbox for correctness.** The inbox is a safety net. Design your system to be resilient to duplicate processing at the domain level as well.

## See Also

- [Overview](./index) -- Messaging setup and `MessagingOptions`
- [Integration Events](./integration-events) -- Define events and handlers
- [Outbox Pattern](./outbox-pattern) -- Reliable message publishing
- [Message Bus](./message-bus) -- The `IMessageBus` API
