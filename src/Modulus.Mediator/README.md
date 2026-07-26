# Modulus.Mediator

Lightweight CQRS mediator for .NET with pipeline behaviors, validation, logging, and a built-in Result pattern.

## Installation

```bash
dotnet add package ModulusKit.Mediator
```

## Setup

```csharp
services.AddModulusMediator();
services.AddModulusHandlers(); // source-generated — registers all handlers and validators

// Add built-in pipeline behaviors (order matters — first registered = outermost).
services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
services.AddPipelineBehavior(typeof(UnitOfWorkBehavior<,>));
```

`AddModulusMediator()` takes no arguments — it registers only the `IMediator` itself. Handler registration comes from the source-generated `AddModulusHandlers()` extension method, which the `ModulusKit.Generators` package emits at compile time for every handler and validator in the compilation (`ICommandHandler<>`, `IQueryHandler<,>`, `IStreamQueryHandler<,>`, `IDomainEventHandler<>`, `AbstractValidator<>`). Reference the generator in each project that defines handlers:

```xml
<PackageReference Include="ModulusKit.Generators" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Solutions scaffolded by the `modulus` CLI have this wired up already.

## Usage

### Define a command and handler

```csharp
public record CreateOrder(string CustomerId, List<OrderItem> Items) : ICommand<Guid>;

public class CreateOrderHandler : ICommandHandler<CreateOrder, Guid>
{
    public async Task<Result<Guid>> Handle(CreateOrder command, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Items);
        await _repository.Add(order, ct);
        return Result<Guid>.Success(order.Id);
    }
}
```

### Define a query and handler

```csharp
public record GetOrderById(Guid Id) : IQuery<OrderDto>;

public class GetOrderByIdHandler : IQueryHandler<GetOrderById, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(GetOrderById query, CancellationToken ct)
    {
        var order = await _repository.GetById(query.Id, ct);
        if (order is null)
            return Error.NotFound("Order.NotFound", "Order was not found");

        return Result<OrderDto>.Success(order.ToDto());
    }
}
```

### Send commands and queries

```csharp
var result = await mediator.Send(new CreateOrder("cust-1", items));

if (result.IsSuccess)
    Console.WriteLine($"Created order: {result.Value}");
else
    Console.WriteLine($"Failed: {result.Errors[0].Description}");
```

## Pipeline Behaviors

Behaviors wrap every request in a middleware-style pipeline. They execute in registration order (first registered = outermost):

| Behavior | Purpose |
|----------|---------|
| `UnhandledExceptionBehavior` | Catches unhandled exceptions and converts them to failure Results |
| `LoggingBehavior` | Logs request start, elapsed time, and success/failure |
| `MetricsBehavior` | Emits `modulus.mediator.handler.duration` histogram per request |
| `TracingBehavior` | Wraps each request in an `Activity` from the `Modulus.Mediator` source, tagging request type and outcome (success / failure with error code / exception). Subscribe with `.AddSource("Modulus.Mediator")` in OpenTelemetry. |
| `ValidationBehavior` | Runs FluentValidation validators and short-circuits on errors |
| `UnitOfWorkBehavior` | Commits an `IUnitOfWork` (resolved from DI; no-op if not registered) after a successful command. Queries bypass. |

### Custom behaviors

Implement `IPipelineBehavior<TRequest, TResponse>` and register it. Behaviors execute in registration order (first registered = outermost):

```csharp
public sealed class AuditBehavior<TRequest, TResponse>(IAuditWriter audit)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess)
            await audit.RecordAsync(typeof(TRequest).Name, cancellationToken).ConfigureAwait(false);
        return response;
    }
}

services.AddPipelineBehavior(typeof(AuditBehavior<,>));
```

### Using `UnitOfWorkBehavior`

Implement `IUnitOfWork` (typically on your `DbContext`) and register it:

```csharp
public class AppDbContext : DbContext, IUnitOfWork
{
    // SaveChangesAsync on DbContext already satisfies IUnitOfWork
}

services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
services.AddPipelineBehavior(typeof(UnitOfWorkBehavior<,>));
```

If no `IUnitOfWork` is registered, the behavior is a no-op — safe to include in every scaffold.

## Domain Events

```csharp
public record OrderPlaced(Guid OrderId, string CustomerId) : IDomainEvent;

public class OrderPlacedHandler : IDomainEventHandler<OrderPlaced>
{
    public async Task Handle(OrderPlaced domainEvent, CancellationToken ct)
    {
        // React to the event
    }
}

// Publish
await mediator.Publish(new OrderPlaced(order.Id, order.CustomerId));
```

### Publish Strategies

By default, `Publish` dispatches to every registered handler **sequentially**, collecting failures into a single `AggregateException`. Configure a different strategy at registration:

```csharp
services.AddModulusMediator(options =>
{
    options.PublishStrategy = PublishStrategy.Parallel; // or StopOnFirstFailure
});
```

| Strategy | Behavior |
|----------|----------|
| `Sequential` (default) | One handler at a time; every handler runs even if earlier ones fail; failures aggregate into one `AggregateException` |
| `Parallel` | Every handler starts concurrently (`Task.WhenAll`); failures still aggregate; cancellation surfaces only after every handler has settled — in-flight handlers are not interrupted |
| `StopOnFirstFailure` | One handler at a time; rethrows the first failure immediately, unwrapped; later handlers never run |

Not configuring `MediatorOptions` (or registering `IMediator` by hand instead of via `AddModulusMediator`) keeps the pre-4.0 default: `Sequential`. See [Domain Events](https://github.com/adamwyatt34/Modulus/blob/main/docs/mediator/domain-events.md#publish-strategies) for the full semantics.

## Learn More

See the [Modulus repository](https://github.com/adamwyatt34/Modulus) for full documentation.
