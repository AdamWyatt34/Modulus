# Modulus.Mediator.Abstractions

Abstractions for the Modulus mediator — interfaces, Result types, and pipeline behavior contracts.

## Installation

```bash
dotnet add package ModulusKit.Mediator.Abstractions
```

## Key Types

### Commands and Queries

```csharp
// Command with no return value
public record CreateOrder(string CustomerId) : ICommand;

// Command with a return value
public record CreateProduct(string Name, decimal Price) : ICommand<Guid>;

// Query
public record GetOrderById(Guid Id) : IQuery<OrderDto>;

// Streaming query
public record GetOrderStream() : IStreamQuery<OrderDto>;

// Domain event
public record OrderCreated(Guid OrderId) : IDomainEvent;
```

### Handlers

```csharp
public class CreateOrderHandler : ICommandHandler<CreateOrder>
{
    public Task<Result> Handle(CreateOrder command, CancellationToken ct)
    {
        // ...
        return Task.FromResult(Result.Success());
    }
}

public class GetOrderByIdHandler : IQueryHandler<GetOrderById, OrderDto>
{
    public Task<Result<OrderDto>> Handle(GetOrderById query, CancellationToken ct)
    {
        // ...
        return Task.FromResult(Result<OrderDto>.Success(dto));
    }
}
```

### Result Pattern

```csharp
// Success
Result.Success();
Result<OrderDto>.Success(dto);

// Failure
Result.Failure(Error.NotFound("Order.NotFound", "Order was not found"));
Result<OrderDto>.Failure(Error.Validation("Order.InvalidId", "ID must not be empty"));

// Implicit conversions
Result result = Error.NotFound("Order.NotFound", "Not found");

// Checking results
if (result.IsSuccess) { /* ... */ }
if (result.IsFailure) { /* inspect result.Errors */ }
```

### Error Types

```csharp
Error.Failure(code, description)      // General failure
Error.Validation(code, description)   // Validation error
Error.NotFound(code, description)     // Resource not found
Error.Conflict(code, description)     // State conflict
Error.Unauthorized(code, description) // Authentication required
Error.Forbidden(code, description)    // Permission denied
```

### Pipeline Behaviors

```csharp
public class TimingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();
        // log elapsed time
        return response;
    }
}
```

## Source Generators & Analyzers

Referencing this package transitively includes the `ModulusKit.Generators` and `ModulusKit.Analyzers` packages as analyzer references. This means your project automatically gets:

- **Source generators** for strongly typed IDs, handler registration, and module auto-discovery
- **Roslyn analyzers** (MOD001--MOD005) for compile-time architecture enforcement

### Strongly Typed IDs

Use the `[StronglyTypedId]` attribute to generate type-safe entity identifiers with full EF Core, JSON, and model binding support:

```csharp
using Modulus.Mediator.Abstractions;

[StronglyTypedId]
public readonly partial record struct OrderId;

[StronglyTypedId(typeof(int))]
public readonly partial record struct SequenceNumber;
```

Supported backing types: `Guid` (default), `int`, `long`.

### Module Ordering

Use the `[ModuleOrder]` attribute to control module initialization order in the auto-discovery generator:

```csharp
[ModuleOrder(1)]
public class CatalogModule : IModuleRegistration { /* ... */ }
```

## Learn More

See the [Modulus repository](https://github.com/adamwyatt34/Modulus) for full documentation.
