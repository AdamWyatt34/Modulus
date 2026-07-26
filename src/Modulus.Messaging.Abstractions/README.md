# Modulus.Messaging.Abstractions

Abstractions for the Modulus messaging system — `IMessageBus`, `IIntegrationEvent`, and outbox pattern interfaces.

## Installation

```bash
dotnet add package ModulusKit.Messaging.Abstractions
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

The `IOutboxStore` interface enables the transactional outbox pattern — events are stored as database rows, then published reliably by a background processor with per-row retry backoff and dead-lettering. (Whether the row commits in the same transaction as your business data depends on how the store is configured — see the [outbox documentation](https://adamwyatt34.github.io/Modulus/messaging/outbox-pattern) for the two supported setups.)

```csharp
public interface IOutboxStore
{
    Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxMessage>> ClaimPending(string ownerId, TimeSpan lease, int batchSize, int maxAttempts, CancellationToken cancellationToken = default);

    Task<int> CountPending(int maxAttempts, CancellationToken cancellationToken = default);

    Task MarkAsProcessed(string ownerId, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task MarkAsFailed(string ownerId, Guid messageId, string error, DateTime? nextAttemptOnUtc, CancellationToken cancellationToken = default);
}
```

`ClaimPending` atomically claims rows for the caller's `ownerId` (a portable, EF-`ExecuteUpdateAsync`-based optimistic claim — no provider-specific row locking), so multiple dispatcher instances polling the same table never publish the same row twice; it skips dead-lettered rows (`Attempts >= maxAttempts`) and rows whose `NextAttemptOnUtc` backoff has not elapsed. `MarkAsProcessed`/`MarkAsFailed` only act on rows `ownerId` still holds the claim on; `MarkAsFailed` records the failure, the next-attempt time computed from the retry policy, and releases the claim so the row is immediately reclaimable once its backoff elapses. A claim's lease (`MessagingOptions.OutboxClaimLease`) expiring — e.g. because the owning instance crashed — is what makes an in-flight row recoverable without operator intervention. The inbox counterpart, `IInboxStore`, provides per-handler idempotent consumption (reserve → execute → mark processed, with reservation release on dead-letter).

## Learn More

See the [Modulus repository](https://github.com/adamwyatt34/Modulus) for full documentation.
