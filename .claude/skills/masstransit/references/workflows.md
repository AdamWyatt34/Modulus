# MassTransit Workflows Reference

## Contents
- Adding a New Integration Event End-to-End
- Switching Transport
- Writing Messaging Tests
- Troubleshooting

---

## Adding a New Integration Event End-to-End

Copy this checklist and track progress:

- [ ] Step 1: Define the event record in the publishing module's Integration project
- [ ] Step 2: Save to outbox inside the command handler (same unit of work as DB write)
- [ ] Step 3: Create `IIntegrationEventHandler<T>` in the subscribing module
- [ ] Step 4: Ensure the subscribing assembly is listed in `MessagingOptions.Assemblies`
- [ ] Step 5: Build — source generator registers the handler automatically
- [ ] Step 6: Write integration tests (outbox save + processor publish + handler receipt)
- [ ] Step 7: Run tests: `dotnet test Modulus.slnx --filter "FullyQualifiedName~Messaging"`

### Step 1 — Event definition

```csharp
// src/Modules/Orders/Integration/OrderShippedEvent.cs
namespace Orders.Integration;

public record OrderShippedEvent(Guid OrderId, string TrackingNumber) : IntegrationEvent;
```

### Step 2 — Save to outbox in handler

```csharp
public sealed class ShipOrderHandler(
    IShipmentRepository repo,
    IOutboxStore outbox) : ICommandHandler<ShipOrderCommand>
{
    public async Task<Result> Handle(ShipOrderCommand cmd, CancellationToken ct)
    {
        var shipment = Shipment.Create(cmd.OrderId, cmd.Carrier);
        await repo.AddAsync(shipment, ct);
        await outbox.Save(new OrderShippedEvent(cmd.OrderId, shipment.TrackingNumber), ct);
        await repo.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

### Step 3 — Handler in subscribing module

```csharp
// src/Modules/Notifications/Application/OrderShippedHandler.cs
namespace Notifications.Application;

public sealed class OrderShippedHandler(IEmailService email)
    : IIntegrationEventHandler<OrderShippedEvent>
{
    public async Task Handle(OrderShippedEvent @event, CancellationToken ct)
        => await email.SendShippingConfirmationAsync(@event.OrderId, @event.TrackingNumber, ct);
}
```

### Step 4 — Assembly registration

```csharp
services.AddModulusMessaging(options =>
{
    options.Transport = Transport.InMemory;
    options.Assemblies =
    [
        typeof(OrdersModule).Assembly,
        typeof(NotificationsModule).Assembly   // ← subscribing module must be here
    ];
});
```

Validate: build succeeds and `IdempotentConsumerAdapter<OrderShippedEvent>` is wired by `AddModulusMessaging`.

---

## Switching Transport

### InMemory → RabbitMQ

```csharp
// appsettings.Production.json
{
  "Messaging": {
    "Transport": "RabbitMq",
    "ConnectionString": "amqp://user:password@rabbitmq-host:5672/vhost"
  }
}
```

```csharp
// Program.cs
services.AddModulusMessaging(options =>
{
    options.Transport = Enum.Parse<Transport>(
        builder.Configuration["Messaging:Transport"]!);
    options.ConnectionString = builder.Configuration["Messaging:ConnectionString"];
    options.Assemblies = [ /* same as before */ ];
});
```

`RabbitMq` requires a non-null `ConnectionString` — `AddModulusMessaging` will throw `InvalidOperationException` at startup if missing.

### InMemory → Azure Service Bus

```csharp
{
  "Messaging": {
    "Transport": "AzureServiceBus",
    "ConnectionString": "Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=..."
  }
}
```

Same registration pattern as RabbitMQ. Queue names are derived from event type name (`queue:{TypeName}`).

---

## Writing Messaging Tests

Use `EF Core InMemory` for outbox/inbox tests. Never use `Transport.RabbitMq` or `Transport.AzureServiceBus` in unit tests.

### Outbox store test

```csharp
public sealed class EfOutboxStoreTests : IDisposable
{
    private readonly OutboxDbContext _ctx;
    private readonly EfOutboxStore _sut;

    public EfOutboxStoreTests()
    {
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _ctx = new OutboxDbContext(options);
        _sut = new EfOutboxStore(_ctx);
    }

    [Fact]
    public async Task Save_ThenGetPending_ReturnsEvent()
    {
        var evt = new TestOrderCreatedEvent { OrderId = Guid.NewGuid(), CustomerName = "Alice" };
        await _sut.Save(evt, CancellationToken.None);

        var pending = await _sut.GetPending(10, CancellationToken.None);

        pending.ShouldHaveSingleItem();
    }

    public void Dispose() => _ctx.Dispose();
}
```

### Handler delivery test (InMemory transport)

```csharp
[Fact]
public async Task Publish_OrderPlacedEvent_HandlerReceivesEvent()
{
    var handler = new TestOrderCreatedHandler();
    var services = new ServiceCollection();
    services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
    services.AddModulusMessaging(opt =>
    {
        opt.Transport = Transport.InMemory;
        opt.Assemblies = [typeof(TestOrderCreatedHandler).Assembly];
    });
    var provider = services.BuildServiceProvider();
    var bus = provider.GetRequiredService<IMessageBus>();

    await bus.Publish(new TestOrderCreatedEvent { OrderId = Guid.NewGuid(), CustomerName = "Bob" });
    await Task.Delay(200);   // allow in-process delivery

    handler.Received.ShouldHaveSingleItem();
}
```

See the **xunit** skill for test fixture conventions (`TestOrderCreatedEvent`, `TestOrderCreatedHandler`).

---

## Troubleshooting

### Handler never called

1. Confirm the handler's assembly is in `MessagingOptions.Assemblies`
2. Rebuild — `AddModulusMessaging` discovers handlers at startup via reflection
3. Check `IdempotentConsumerAdapter<TEvent>` is registered: inspect `ServiceProvider` in tests
4. With InMemory transport, add a short `Task.Delay` after `Publish` — delivery is async

### Outbox messages stuck (not published)

1. `OutboxProcessor` polls every `OutboxPollInterval` (default 5s) — wait or reduce interval in tests
2. Check `ProcessedAt IS NULL` in the outbox table — if rows are marked processed, the processor ran but something else consumed them
3. If event type cannot be resolved (`Type.GetType()` returns null), processor logs a warning and skips — this happens after renaming event classes with existing rows in the DB

### "Connection string required" at startup

`Transport.RabbitMq` and `Transport.AzureServiceBus` both validate `ConnectionString != null` in `AddModulusMessaging`. Set the connection string before calling `Build()` on the host, or use `Transport.InMemory` for local runs.

### Duplicate handler execution in tests

The inbox deduplication stores `EventId + HandlerTypeName`. If you reuse event instances across test cases, the second test sees `HasBeenProcessed = true` and skips the handler. Always create a new event instance per test — `IntegrationEvent` generates a new `EventId` on construction.

```csharp
// BAD — shared instance, second test skipped by inbox
private static readonly TestOrderCreatedEvent SharedEvent = new() { ... };

// GOOD — new EventId each test
var evt = new TestOrderCreatedEvent { OrderId = Guid.NewGuid(), CustomerName = "Alice" };
```
