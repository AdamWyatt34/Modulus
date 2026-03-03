# Modulus.Mediator

Lightweight CQRS mediator for .NET with pipeline behaviors, validation, logging, and a built-in Result pattern.

## Installation

```bash
dotnet add package ModulusKit.Mediator
```

## Setup

```csharp
services.AddModulusMediator(typeof(Program).Assembly);

// Add built-in pipeline behaviors
services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
```

`AddModulusMediator` scans the provided assemblies and auto-registers all handlers (`ICommandHandler<>`, `IQueryHandler<,>`, `IStreamQueryHandler<,>`, `IDomainEventHandler<>`).

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
| `ValidationBehavior` | Runs FluentValidation validators and short-circuits on errors |

### Custom behaviors

```csharp
public class UnitOfWorkBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;

    public UnitOfWorkBehavior(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        // Commit only on success (TResponse is always Result or Result<T>)
        if (response is Result { IsSuccess: true })
            await _unitOfWork.CommitAsync(cancellationToken);

        return response;
    }
}
```

Register custom behaviors:

```csharp
services.AddPipelineBehavior(typeof(UnitOfWorkBehavior<,>));
```

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

## Learn More

See the [Modulus repository](https://github.com/adamwyatt34/Modulus) for full documentation.
