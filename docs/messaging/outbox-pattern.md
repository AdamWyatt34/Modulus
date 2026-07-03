# Outbox Pattern

The transactional outbox pattern solves the **dual-write problem** -- the challenge of atomically updating your database and publishing a message to a broker. Modulus provides a built-in outbox implementation that saves messages to your database within the same transaction as your domain changes, then reliably publishes them to the broker via a background processor.

## The Problem

Consider a command handler that saves an order and publishes an event:

```csharp
// Danger: two separate operations that can partially fail
await _orderRepository.AddAsync(order, ct);          // 1. Write to database
await _messageBus.Publish(new OrderCreatedEvent(...), ct);  // 2. Publish to broker
```

Several failure scenarios can occur:

1. **Database succeeds, broker fails** -- The order is saved but the event is never published. Other modules never learn about the order.
2. **Broker succeeds, database fails** -- The event is published but the order is not saved. Consumers process a phantom event.
3. **Broker is temporarily unavailable** -- The entire operation fails even though the database write was valid.

The outbox pattern eliminates these issues by writing the event to the same database in the same transaction as the domain change.

## How It Works

```mermaid
sequenceDiagram
    participant Handler as Command Handler
    participant DB as Database
    participant Outbox as OutboxProcessor
    participant Bus as Transport
    participant Consumer as Consumer

    Handler->>DB: BEGIN TRANSACTION
    Handler->>DB: Save entity changes
    Handler->>DB: Save OutboxMessage
    Handler->>DB: COMMIT

    loop Every OutboxPollInterval
        Outbox->>DB: GetPending(batchSize)
        DB-->>Outbox: Pending messages
        Outbox->>Bus: Publish deserialized events
        Bus->>Consumer: Deliver messages
        Outbox->>DB: MarkAsProcessed(messageIds)
    end
```

1. **Command handler** saves the domain entity and an `OutboxMessage` in the **same database transaction**.
2. The transaction commits atomically -- either both the entity and the outbox message are saved, or neither is.
3. **OutboxProcessor** (a `BackgroundService`) polls the database on a configurable interval for pending outbox messages.
4. For each batch, it deserializes the events and publishes them through the configured transport.
5. After successful publishing, the messages are marked as processed.

## IOutboxStore Interface

The `IOutboxStore` interface defines the contract for outbox persistence:

<!-- verify -->
```csharp
public interface IOutboxStore
{
    Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, int maxAttempts, CancellationToken cancellationToken = default);
    Task<int> CountPending(int maxAttempts, CancellationToken cancellationToken = default);
    Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task MarkAsFailed(Guid messageId, string error, CancellationToken cancellationToken = default);
}
```

| Method | Description |
|---|---|
| `Save` | Serializes and saves an integration event as an `OutboxMessage`. |
| `GetPending` | Retrieves up to `batchSize` unprocessed messages whose attempt count is below `maxAttempts`, ordered by creation time. Dead-lettered rows are excluded so they do not starve newer rows. |
| `CountPending` | Counts the same population `GetPending` draws from. Used by the backlog-depth health check. |
| `MarkAsProcessed` | Marks the specified messages as processed so they are not picked up again. |
| `MarkAsFailed` | Increments a message's attempt counter and records the failure message. |

## OutboxMessage Model

Each outbox entry is stored as an `OutboxMessage`:

<!-- verify -->
```csharp
public sealed class OutboxMessage
{
    public required Guid Id { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
```

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Unique identifier for the outbox entry. |
| `EventType` | `string` | Assembly-qualified type name of the event, used for deserialization. |
| `Payload` | `string` | JSON-serialized event data. |
| `CreatedAt` | `DateTime` | When the outbox message was created. |
| `ProcessedAt` | `DateTime?` | When the message was successfully published. `null` while pending. |
| `Attempts` | `int` | Number of failed publish attempts. Once it reaches `RetryPolicy.MaxAttempts` the message is dead-lettered. |
| `LastError` | `string?` | Error message from the most recent failed publish attempt. |

## EfOutboxStore

The `EfOutboxStore` is the built-in Entity Framework Core implementation of `IOutboxStore`. It uses your application's `DbContext` to persist outbox messages, which means the outbox write participates in the same EF Core transaction as your domain entity changes.

```csharp
// The EfOutboxStore is registered automatically when you configure EF Core
// with your DbContext. Just ensure your DbContext includes the OutboxMessage entity.
```

::: info Same DbContext, same transaction
Because `EfOutboxStore` operates on the same `DbContext` as your repositories, calling `Save` on the outbox store and calling `SaveChangesAsync` on the `DbContext` are part of the same transaction. This is the key guarantee that makes the outbox pattern work.
:::

## OutboxProcessor

The `OutboxProcessor` is a `BackgroundService` that runs continuously in your application. It polls the outbox store at a configurable interval, deserializes pending events, publishes them through the configured transport, and marks them as processed.

**Processing flow:**

1. Wait for `OutboxPollInterval` (default: 5 seconds).
2. Call `IOutboxStore.GetPending(batchSize)` to retrieve up to `OutboxBatchSize` (default: 100) pending messages.
3. For each message, deserialize the `Payload` using the `EventType` to resolve the concrete type.
4. Publish each deserialized event through the configured transport. On RabbitMQ, publisher confirmations are enabled, so a publish only counts as successful once the broker confirms it.
5. Call `IOutboxStore.MarkAsProcessed(ids)` for all successfully published messages.
6. Repeat.

### Configuration

Control the polling interval and batch size through `MessagingOptions`:

<!-- verify -->
```csharp
builder.Services.AddModulusRabbitMqTransport();
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.RabbitMq;
    options.ConnectionString = builder.Configuration.GetConnectionString("RabbitMq");
    options.Assemblies.Add(typeof(Program).Assembly);

    // Outbox configuration
    options.OutboxPollInterval = TimeSpan.FromSeconds(10); // Default: 5 seconds
    options.OutboxBatchSize = 50;                          // Default: 100
});
```

| Option | Default | Description |
|---|---|---|
| `OutboxPollInterval` | `5 seconds` | How frequently the processor checks for pending messages. Minimum: 1 second. Lower values reduce latency; higher values reduce database load. |
| `OutboxBatchSize` | `100` | Maximum messages processed per cycle. Valid range: 1–1000. Tune based on your throughput requirements. |

## Usage Example

The typical pattern is to save your domain entities and write to the outbox within the same unit of work:

```csharp
public sealed class PlaceOrderCommandHandler
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOutboxStore _outboxStore;
    private readonly IUnitOfWork _unitOfWork;

    public PlaceOrderCommandHandler(
        IOrderRepository orderRepository,
        IOutboxStore outboxStore,
        IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _outboxStore = outboxStore;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        var order = new Order(command.CustomerId, command.Items);

        // 1. Save the order
        await _orderRepository.AddAsync(order, cancellationToken);

        // 2. Save the integration event to the outbox (same DbContext)
        await _outboxStore.Save(
            new OrderCreatedEvent(
                order.Id,
                command.CustomerId,
                order.Total,
                order.Items.Select(i =>
                    new OrderItemDto(i.ProductId, i.Quantity)).ToList()));

        // 3. Commit both in a single transaction
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // The OutboxProcessor will pick up the event and publish it
        return order.Id;
    }
}
```

::: warning Do not publish directly when using the outbox
When using the outbox pattern, save events to the outbox store instead of calling `IMessageBus.Publish()` directly. Calling `Publish` directly bypasses the outbox and reintroduces the dual-write problem.
:::

## How the OutboxProcessor Recovers from Failures

The outbox pattern is inherently resilient:

- **Broker unavailable:** The `OutboxProcessor` catches publish failures and retries on the next polling cycle. Messages remain in the outbox until successfully published.
- **Application crash after commit:** The outbox messages are persisted in the database. When the application restarts, the `OutboxProcessor` picks up where it left off.
- **Application crash before commit:** The transaction rolls back. Neither the domain entity nor the outbox message is persisted, which is the correct behavior.
- **Duplicate publishing:** If the application crashes after publishing but before marking messages as processed, the same messages may be published again on the next cycle. Use the [Inbox Pattern](./inbox-pattern) on the consumer side to handle this.

```mermaid
flowchart TD
    A[OutboxProcessor polls] --> B{Pending messages?}
    B -->|No| A
    B -->|Yes| C[Deserialize events]
    C --> D[Publish via transport]
    D --> E{Publish succeeded?}
    E -->|Yes| F[Mark as processed]
    F --> A
    E -->|No| G[Log error]
    G --> A
```

## Best Practices

- **Always save to the outbox within the same transaction as your domain changes.** This is the entire point of the pattern. If you save the outbox message in a separate transaction, you lose the atomicity guarantee.
- **Tune `OutboxPollInterval` for your latency requirements.** A 1-second interval gives near-real-time delivery but increases database load. A 30-second interval reduces load but adds latency.
- **Monitor the outbox table.** If `ProcessedAt` is `null` for a large number of old messages, the processor may be failing silently. Set up alerts for outbox backlog.
- **Pair with the inbox pattern.** The outbox guarantees at-least-once publishing. Use the [Inbox Pattern](./inbox-pattern) on the consumer side to achieve exactly-once processing.
- **Clean up processed messages.** Over time, the outbox table grows. Implement a periodic job to delete or archive messages where `ProcessedAt` is not null and older than a retention period.

## See Also

- [Overview](./index) -- Messaging setup and `MessagingOptions`
- [Integration Events](./integration-events) -- Define events and handlers
- [Message Bus](./message-bus) -- The `IMessageBus` API
- [Inbox Pattern](./inbox-pattern) -- Idempotent consumption to complement the outbox
