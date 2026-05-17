# EF Core Patterns Reference

## Contents
- DbContext Design
- Entity Models
- Store Implementations
- Idempotency Patterns
- Anti-Patterns

---

## DbContext Design

Each DbContext owns exactly one bounded schema. `OutboxDbContext` knows nothing about inbox, and vice versa.
This is intentional — mixing concerns into a single messaging DbContext would couple unrelated processing pipelines.

```csharp
// GOOD — narrow, single-purpose context
public class OutboxDbContext : DbContext
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    // Only outbox schema here
}

// BAD — never merge outbox + inbox into one context
public class MessagingDbContext : DbContext
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>(); // ❌ couples pipelines
}
```

**Why:** Outbox and inbox processors run independently on different schedules. Shared context means shared migrations, shared connection strings, and shared failure modes.

---

## Entity Models

Models live in `src/Modulus.Messaging.Abstractions/` to avoid circular dependencies.
They are sealed records with init-only properties.

```csharp
// OutboxMessage — stores serialized integration event + type metadata
public sealed record OutboxMessage
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = null!;   // AssemblyQualifiedName for Type.GetType()
    public string Payload { get; init; } = null!;      // JSON-serialized event body
    public DateTime CreatedAt { get; init; }
    public DateTime? ProcessedAt { get; init; }        // null = pending
}

// InboxMessageConsumer — composite key for per-handler idempotency
public sealed record InboxMessageConsumer
{
    public Guid InboxMessageId { get; init; }
    public string Name { get; init; } = null!;         // handler type name
}
```

Entity configurations live in `OnModelCreating`, not in separate `IEntityTypeConfiguration<T>` classes.
This keeps small schemas readable without unnecessary file proliferation.

```csharp
// GOOD for narrow schemas — inline configuration
modelBuilder.Entity<OutboxMessage>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.EventType).IsRequired().HasMaxLength(500);
    entity.Property(e => e.Payload).IsRequired();
});

// GOOD when schema grows complex — separate configuration class
// Use IEntityTypeConfiguration<T> only when OnModelCreating exceeds ~30 lines
```

---

## Store Implementations

Both stores are `sealed` classes registered as `Scoped`. They depend only on their respective DbContext.

```csharp
// EfOutboxStore — primary constructor, sealed, scoped lifetime
public sealed class EfOutboxStore(OutboxDbContext context) : IOutboxStore
{
    public async Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = @event.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(@event, @event.GetType()),
            CreatedAt = DateTime.UtcNow
        };
        await context.OutboxMessages.AddAsync(message, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await context.OutboxMessages
            .Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, now), cancellationToken);
    }
}
```

**Note:** `ExecuteUpdateAsync` is preferred over load-then-update for batch operations — it avoids loading entities into memory just to set a timestamp.

---

## Idempotency Patterns

### Per-handler deduplication

The inbox uses a two-table design: `InboxMessages` (the event) + `InboxMessageConsumers` (which handlers ran).
This allows the same event to be consumed by multiple independent handlers while each is individually idempotent.

```csharp
// Check before executing handler
public async Task<bool> HasBeenProcessed(Guid messageId, string handlerName, CancellationToken ct)
    => await context.InboxMessageConsumers
        .AnyAsync(c => c.InboxMessageId == messageId && c.Name == handlerName, ct);

// Record after successful execution
public async Task RecordConsumer(Guid messageId, string handlerName, CancellationToken ct)
{
    context.InboxMessageConsumers.Add(new InboxMessageConsumer
    {
        InboxMessageId = messageId,
        Name = handlerName
    });
    await context.SaveChangesAsync(ct);
}
```

### Concurrent duplicate handling

When two consumer instances receive the same event simultaneously, `AnyAsync` may both return false.
Catch `DbUpdateException` on insert and clear the ChangeTracker — never rethrow, as the goal (exactly-once) is already met.

```csharp
try
{
    await context.InboxMessages.AddAsync(inboxMessage, cancellationToken);
    await context.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateException)
{
    context.ChangeTracker.Clear(); // critical — prevents poisoned entity state
}
```

---

## Anti-Patterns

### WARNING: Sharing a DbContext Across Processor and Store

**The Problem:**

```csharp
// BAD — OutboxProcessor and EfOutboxStore sharing the same DbContext instance
services.AddSingleton<OutboxDbContext>(...); // ❌ Singleton DbContext
```

**Why This Breaks:**
1. DbContext is not thread-safe — concurrent access corrupts ChangeTracker
2. Singleton scope outlives request scope, causing stale data reads
3. Exceptions in one operation leave the context in a faulted state for all future operations

**The Fix:**

```csharp
// GOOD — scoped DbContext, processor creates a new scope per batch
services.AddDbContext<OutboxDbContext>(...); // Scoped by default
// OutboxProcessor resolves IServiceScopeFactory and creates scope per poll cycle
```

---

### WARNING: Missing ChangeTracker.Clear() After DbUpdateException

**The Problem:**

```csharp
catch (DbUpdateException)
{
    // Swallow and continue — entity still tracked in error state ❌
}
```

**Why This Breaks:**
1. The failed entity remains in `Added` state in the ChangeTracker
2. Next `SaveChangesAsync` call will attempt to insert it again
3. Results in repeated exceptions or data corruption

**The Fix:**

```csharp
catch (DbUpdateException)
{
    context.ChangeTracker.Clear(); // Reset all tracked entities
}
```

---

### WARNING: Using Type.GetType() Without AssemblyQualifiedName

**The Problem:**

```csharp
// BAD — stores only the short type name
EventType = @event.GetType().Name // "UserCreatedEvent"
// Later: Type.GetType("UserCreatedEvent") returns null
```

**Why This Breaks:** `Type.GetType()` requires the assembly-qualified name to resolve types across assemblies.

**The Fix:**

```csharp
// GOOD — full qualified name
EventType = @event.GetType().AssemblyQualifiedName! // "MyApp.Events.UserCreatedEvent, MyApp, Version=1.0.0..."
// Later: Type.GetType(eventType) resolves correctly
```
