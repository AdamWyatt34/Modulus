---
name: moduluskit
description: Builds .NET modular monoliths with the ModulusKit NuGet packages — custom CQRS mediator with the Result pattern, event-driven messaging with transactional outbox/inbox, source-generated handler registration, and the modulus CLI. Use when installing or consuming ModulusKit.* packages, writing commands/queries/handlers that return Result, wiring integration events across modules, or scaffolding solutions and modules with the modulus tool.
---

# ModulusKit

A NuGet library ecosystem (nine packages, one coordinated version) for building .NET modular monoliths with a custom CQRS mediator, event-driven messaging with transactional outbox/inbox, and compile-time handler registration. No MediatR, no MassTransit.

## Packages

| Package | Install | Purpose |
|---------|---------|---------|
| `ModulusKit.Mediator.Abstractions` | `dotnet add package ModulusKit.Mediator.Abstractions` | `ICommand`, `IQuery`, `Result`, `Error`, `IUnitOfWork` |
| `ModulusKit.Mediator` | `dotnet add package ModulusKit.Mediator` | `AddModulusMediator()`, pipeline behaviors |
| `ModulusKit.Messaging.Abstractions` | `dotnet add package ModulusKit.Messaging.Abstractions` | `IIntegrationEvent`, `IMessageBus`, `IOutboxStore` |
| `ModulusKit.Messaging` | `dotnet add package ModulusKit.Messaging` | In-house transport layer, in-memory transport, outbox/inbox, health checks, metrics |
| `ModulusKit.Messaging.RabbitMq` | `dotnet add package ModulusKit.Messaging.RabbitMq` | RabbitMQ transport — `AddModulusRabbitMqTransport()` |
| `ModulusKit.Messaging.AzureServiceBus` | `dotnet add package ModulusKit.Messaging.AzureServiceBus` | Azure Service Bus transport — `AddModulusAzureServiceBusTransport()` |
| `ModulusKit.Generators` | `dotnet add package ModulusKit.Generators` | Source-generated `AddModulusHandlers()`, `AddAllModules()`, `MapAllModuleEndpoints()` |
| `ModulusKit.Analyzers` | `dotnet add package ModulusKit.Analyzers` | Compile-time rules MOD001–MOD005 |
| `ModulusKit.Cli` | `dotnet tool install -g ModulusKit.Cli` | `modulus init`, `add-module`, `doctor`, `dlq`, etc. |

All nine ship at one aligned version; `modulus upgrade` bumps every pin and `modulus doctor` warns on version skew.

## Quick Start — Mediator (CQRS + Result Pattern)

### 1. Install packages

```xml
<PackageReference Include="ModulusKit.Mediator.Abstractions" />
<PackageReference Include="ModulusKit.Mediator" />
<PackageReference Include="ModulusKit.Generators" />
```

### 2. Define a command

```csharp
using Modulus.Mediator.Abstractions;

namespace MyApp.Orders;

// Command with no return value
public record PlaceOrderCommand(Guid CustomerId, List<OrderItem> Items) : ICommand;

// Command with a return value
public record CreateProductCommand(string Name, decimal Price) : ICommand<Guid>;
```

### 3. Implement the handler

```csharp
using Modulus.Mediator.Abstractions;

namespace MyApp.Orders;

public sealed class PlaceOrderCommandHandler(IOrderRepository repo)
    : ICommandHandler<PlaceOrderCommand>
{
    public async Task<Result> Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        if (command.Items.Count == 0)
            return Error.Validation("Orders.EmptyItems", "Order must have at least one item.");

        var order = Order.Create(command.CustomerId, command.Items);
        await repo.AddAsync(order, ct);

        return Result.Success();
    }
}
```

### 4. Register in DI

```csharp
// Program.cs — AddModulusMediator registers zero behaviors; every behavior is opt-in,
// and registration order = execution order (first = outermost).
builder.Services.AddModulusMediator();
builder.Services.AddModulusHandlers();  // source-generated — discovers all handlers automatically
builder.Services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(UnitOfWorkBehavior<,>));  // commits IUnitOfWork after successful commands
```

### 5. Dispatch from an endpoint

```csharp
app.MapPost("/orders", async (PlaceOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(cmd, ct);
    return result.Match(
        () => Results.Created(),
        failure => Results.Problem(failure.Errors.First().Description));
});
```

`IMediator` members: `Send(ICommand)`, `Send<TResult>(ICommand<TResult>)`, `Query<TResult>(IQuery<TResult>)`, `Stream<TResult>(IStreamQuery<TResult>)`, `Publish<TEvent>` (domain events, in-process only).

## Quick Start — Messaging (Integration Events)

### 1. Install packages

```xml
<PackageReference Include="ModulusKit.Messaging.Abstractions" />
<PackageReference Include="ModulusKit.Messaging" />
<!-- Plus ONE broker transport package for production (in-memory needs none): -->
<PackageReference Include="ModulusKit.Messaging.RabbitMq" />
```

### 2. Define an integration event — inherit the record base class

```csharp
using Modulus.Messaging.Abstractions;

namespace MyApp.Orders.IntegrationEvents;

// IntegrationEvent supplies EventId, OccurredOn, and CorrelationId with defaults
public sealed record OrderPlacedEvent(Guid OrderId, Guid CustomerId, decimal Total)
    : IntegrationEvent;
```

### 3. Publish via the outbox

```csharp
public sealed class PlaceOrderCommandHandler(IOrderRepository repo, IOutboxStore outbox)
    : ICommandHandler<PlaceOrderCommand>
{
    public async Task<Result> Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Items);
        await repo.AddAsync(order, ct);

        // Stored in the database, not sent to the broker — reliable delivery guaranteed.
        // Committed rows wake the outbox processor immediately (change notification);
        // polling remains only as a fallback sweep.
        await outbox.Save(new OrderPlacedEvent(order.Id, command.CustomerId, order.Total), ct);

        return Result.Success();
    }
}
```

### 4. Handle in another module

```csharp
using Modulus.Messaging.Abstractions;

namespace MyApp.Loyalty;

public sealed class OrderPlacedEventHandler(ILoyaltyService loyalty)
    : IIntegrationEventHandler<OrderPlacedEvent>
{
    public async Task Handle(OrderPlacedEvent @event, CancellationToken ct)
    {
        await loyalty.AwardPointsAsync(@event.CustomerId, @event.Total, ct);
    }
}
```

### 5. Register messaging

```csharp
builder.Services.AddModulusRabbitMqTransport();   // broker transports need one extra call
builder.Services.AddModulusMessaging(builder.Configuration, options =>  // binds the "Messaging" section first
{
    options.Transport = Transport.RabbitMq;  // or InMemory, AzureServiceBus
    options.ConnectionString = builder.Configuration.GetConnectionString("RabbitMq");
    options.Assemblies.Add(typeof(OrderPlacedEvent).Assembly);
    options.Assemblies.Add(typeof(OrderPlacedEventHandler).Assembly);
});
builder.Services.AddModulusOutbox(o => o.UseSqlServer(connectionString));  // outbox persistence
builder.Services.AddModulusInbox(o => o.UseSqlServer(connectionString));   // consumer idempotency
builder.Services.AddHealthChecks().AddModulusMessaging();  // broker + outbox backlog checks, tagged "ready"
```

## Key Concepts

| Concept | Interface | Returns | Pipeline |
|---------|-----------|---------|----------|
| Command (no value) | `ICommand` / `ICommandHandler<T>` | `Task<Result>` | Yes |
| Command (with value) | `ICommand<TResult>` / `ICommandHandler<T, TResult>` | `Task<Result<TResult>>` | Yes |
| Query | `IQuery<TResult>` / `IQueryHandler<T, TResult>` | `Task<Result<TResult>>` | Yes |
| Stream query | `IStreamQuery<TResult>` / `IStreamQueryHandler<T, TResult>` | `IAsyncEnumerable<TResult>` | No (bypasses) |
| Domain event | `IDomainEvent` / `IDomainEventHandler<T>` | `Task` | No |
| Integration event | `IntegrationEvent` record / `IIntegrationEventHandler<T>` | `Task` | No |

## See Also

- [patterns](references/patterns.md) — Result pattern, error types, pipeline behaviors, outbox/inbox mechanics, immediate dispatch, health checks, metrics, analyzers, anti-patterns
- [workflows](references/workflows.md) — CLI command reference, adding commands/queries/events end-to-end, DI registration checklist, testing conventions, troubleshooting
