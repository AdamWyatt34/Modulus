# EF Core Workflows Reference

## Contents
- Adding a New Store
- Writing Messaging Tests with InMemory
- DI Registration
- Checklist: New Outbox/Inbox Entity

---

## Adding a New Store

Follow this exact sequence when extending the outbox/inbox infrastructure with a new EF-backed store.

Copy this checklist and track progress:
- [ ] Step 1: Define the entity record in `src/Modulus.Messaging.Abstractions/`
- [ ] Step 2: Define the store interface in `src/Modulus.Messaging.Abstractions/`
- [ ] Step 3: Add `DbSet<T>` property and `OnModelCreating` config to the appropriate DbContext
- [ ] Step 4: Implement the store as a `sealed` class with primary constructor
- [ ] Step 5: Register the store as `Scoped` in `ServiceCollectionExtensions`
- [ ] Step 6: Write xUnit tests using EF Core InMemory provider

**Step 1 — Entity in Abstractions:**

```csharp
// src/Modulus.Messaging.Abstractions/Outbox/OutboxMessage.cs
public sealed record OutboxMessage
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = null!;
    public string Payload { get; init; } = null!;
    public DateTime CreatedAt { get; init; }
    public DateTime? ProcessedAt { get; init; }
}
```

**Step 2 — Interface in Abstractions:**

```csharp
// src/Modulus.Messaging.Abstractions/Outbox/IOutboxStore.cs
public interface IOutboxStore
{
    Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, CancellationToken cancellationToken = default);
    Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
```

**Step 4 — Sealed implementation:**

```csharp
// src/Modulus.Messaging/Outbox/EfOutboxStore.cs
public sealed class EfOutboxStore(OutboxDbContext context) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, CancellationToken ct)
        => await context.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
}
```

**Step 5 — Register as Scoped:**

```csharp
// src/Modulus.Messaging/DependencyInjection/ServiceCollectionExtensions.cs
services.AddScoped<IOutboxStore, EfOutboxStore>();
// NOTE: Consumer registers the DbContext — Modulus does not call AddDbContext<OutboxDbContext> here
```

---

## Writing Messaging Tests with InMemory

See the **xunit** skill for general test conventions. For EF Core InMemory tests specifically:

### Isolation pattern — unique database per test

```csharp
// tests/Modulus.Messaging.Tests/EfOutboxStoreTests.cs
private static IServiceProvider BuildServices()
{
    var services = new ServiceCollection();
    services.AddDbContext<OutboxDbContext>(options =>
        options.UseInMemoryDatabase($"OutboxTests_{Guid.NewGuid()}")); // unique per test
    services.AddScoped<IOutboxStore, EfOutboxStore>();
    return services.BuildServiceProvider();
}

[Fact]
public async Task Save_StoresEvent_AsOutboxMessage()
{
    await using var provider = BuildServices();
    using var scope = provider.CreateScope();
    var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

    await store.Save(new TestIntegrationEvent(), CancellationToken.None);

    var context = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    context.OutboxMessages.Should().HaveCount(1);
}
```

### Shared database for cross-scope tests (OutboxProcessor pattern)

```csharp
// When the processor needs to read what the store wrote in another scope
var root = new InMemoryDatabaseRoot();

services.AddDbContext<OutboxDbContext>(options =>
    options.UseInMemoryDatabase("SharedOutboxTest", root));
```

Use `InMemoryDatabaseRoot` only when testing background processor behavior that reads from one scope and marks in another. For single-scope tests, prefer unique database names.

### InMemory limitations — explicit asserts required

```csharp
// InMemory doesn't enforce FK constraints, MaxLength, or Required
// Always assert field content explicitly, not just row count

var message = await context.OutboxMessages.SingleAsync();
message.EventType.ShouldNotBeNullOrEmpty();      // InMemory won't reject null EventType
message.ProcessedAt.ShouldBeNull();               // Verify initial state explicitly
```

---

## DI Registration

`AddModulusMessaging()` registers stores but NOT DbContexts. Consumers are responsible for their own DbContext registration.

```csharp
// Consumer application's DI setup
services.AddDbContext<OutboxDbContext>(options =>
    options.UseSqlServer(connectionString));

services.AddDbContext<InboxDbContext>(options =>
    options.UseSqlServer(connectionString));

// Then wire Modulus stores
services.AddModulusMessaging(options => { ... });
```

**Why:** Modulus doesn't know which provider (SQL Server, PostgreSQL, etc.) the consumer uses. Forcing DbContext registration inside `AddModulusMessaging` would impose a provider choice.

### Validate registration in tests

```csharp
[Fact]
public void AddModulusMessaging_RegistersExpectedServices()
{
    var services = new ServiceCollection();
    services.AddDbContext<OutboxDbContext>(o => o.UseInMemoryDatabase("test"));
    services.AddDbContext<InboxDbContext>(o => o.UseInMemoryDatabase("test"));
    services.AddModulusMessaging();

    var provider = services.BuildServiceProvider();

    provider.GetRequiredService<IOutboxStore>().ShouldBeOfType<EfOutboxStore>();
    provider.GetRequiredService<IInboxStore>().ShouldBeOfType<EfInboxStore>();
}
```

---

## Checklist: New Outbox/Inbox Entity

Use when adding a new trackable entity to the outbox or inbox schema.

Copy this checklist and track progress:
- [ ] Entity record defined in `src/Modulus.Messaging.Abstractions/` (sealed record, init-only)
- [ ] `HasKey` configured (single column or composite)
- [ ] String properties have `HasMaxLength` (prevents unbounded columns in SQL providers)
- [ ] Nullable `DateTime?` used for processing timestamps, not `bool IsProcessed`
- [ ] `DbSet<T>` property added to the correct DbContext
- [ ] Store method queries use `OrderBy` before `Take` (avoid non-deterministic batching)
- [ ] `ChangeTracker.Clear()` added after any `DbUpdateException` catch
- [ ] Test added with `UseInMemoryDatabase($"..._{Guid.NewGuid()}")`
- [ ] Verify test isolation: no shared state between test methods

### Validate your setup

```powershell
# Run only messaging tests
dotnet test Modulus.slnx --filter "FullyQualifiedName~Messaging"
```

If tests fail with "database locked" or "entity already tracked" errors, check:
1. Each test uses a unique database name
2. `DbContext` is scoped (not singleton)
3. `ChangeTracker.Clear()` is called after caught `DbUpdateException`
