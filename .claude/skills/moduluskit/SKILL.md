# ModulusKit

A NuGet library ecosystem for building .NET modular monoliths with custom CQRS mediator, event-driven messaging with transactional outbox/inbox, and compile-time handler registration.

**Use when:** building a .NET modular monolith, adding CQRS with the Result pattern, wiring cross-module integration events, or scaffolding a new solution with the `modulus` CLI tool.

## Packages

| Package | Install | Purpose |
|---------|---------|---------|
| `ModulusKit.Mediator.Abstractions` | `dotnet add package ModulusKit.Mediator.Abstractions` | `ICommand`, `IQuery`, `Result`, `Error` interfaces |
| `ModulusKit.Mediator` | `dotnet add package ModulusKit.Mediator` | `AddModulusMediator()`, pipeline behaviors |
| `ModulusKit.Messaging.Abstractions` | `dotnet add package ModulusKit.Messaging.Abstractions` | `IIntegrationEvent`, `IMessageBus`, `IOutboxStore` |
| `ModulusKit.Messaging` | `dotnet add package ModulusKit.Messaging` | MassTransit integration, outbox processor |
| `ModulusKit.Generators` | `dotnet add package ModulusKit.Generators` | Source-generated `AddModulusHandlers()` |
| `ModulusKit.Analyzers` | `dotnet add package ModulusKit.Analyzers` | Compile-time rules MOD001-MOD005 |
| `ModulusKit.Cli` | `dotnet tool install -g ModulusKit.Cli` | `modulus init`, `modulus add-module`, etc. |

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
// Program.cs
builder.Services.AddModulusMediator();
builder.Services.AddModulusHandlers();  // source-generated — discovers all handlers automatically
builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
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

## Quick Start — Messaging (Integration Events)

### 1. Install packages

```xml
<PackageReference Include="ModulusKit.Messaging.Abstractions" />
<PackageReference Include="ModulusKit.Messaging" />
```

### 2. Define an integration event

```csharp
using Modulus.Messaging.Abstractions;

namespace MyApp.Orders.IntegrationEvents;

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

        // Stored in the same transaction as the order — reliable delivery guaranteed
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
builder.Services.AddModulusMessaging(options =>
{
    options.Transport = Transport.RabbitMq;  // or InMemory, AzureServiceBus
    options.ConnectionString = builder.Configuration.GetConnectionString("RabbitMq");
    options.Assemblies.Add(typeof(OrderPlacedEvent).Assembly);
    options.Assemblies.Add(typeof(OrderPlacedEventHandler).Assembly);
});
```

## Key Concepts

| Concept | Interface | Returns | Pipeline |
|---------|-----------|---------|----------|
| Command (no value) | `ICommand` / `ICommandHandler<T>` | `Task<Result>` | Yes |
| Command (with value) | `ICommand<TResult>` / `ICommandHandler<T, TResult>` | `Task<Result<TResult>>` | Yes |
| Query | `IQuery<TResult>` / `IQueryHandler<T, TResult>` | `Task<Result<TResult>>` | Yes |
| Stream query | `IStreamQuery<TResult>` / `IStreamQueryHandler<T, TResult>` | `IAsyncEnumerable<TResult>` | No (bypasses) |
| Domain event | `IDomainEvent` / `IDomainEventHandler<T>` | `Task` | No |
| Integration event | `IIntegrationEvent` / `IIntegrationEventHandler<T>` | `Task` | No |

## See Also

- [patterns](references/patterns.md) — Result pattern, error types, pipeline behaviors, implicit conversions, outbox/inbox
- [workflows](references/workflows.md) — adding commands/queries/events, scaffolding, DI registration, testing
