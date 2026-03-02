# Commands & Queries

Modulus follows the CQRS (Command Query Responsibility Segregation) pattern. Commands represent intent to change state. Queries represent intent to read state. Each has a dedicated interface and handler contract, and all dispatch goes through the `IMediator` interface.

## Commands

### ICommand (No Return Value)

Use `ICommand` when the command performs a side effect but does not need to return data to the caller:

```csharp
public interface ICommand;
```

**Example -- delete a product:**

```csharp
public sealed record DeleteProductCommand(Guid ProductId) : ICommand;
```

### ICommand\<TResult\> (With Return Value)

Use `ICommand<TResult>` when the command produces a value that the caller needs, such as a newly created entity's identifier:

```csharp
public interface ICommand<TResult>;
```

**Example -- create a product and return its ID:**

```csharp
public sealed record CreateProductCommand(
    string Name,
    decimal Price,
    string Sku) : ICommand<Guid>;
```

## Queries

### IQuery\<TResult\>

Queries are read-only operations that return data. They always produce a `Result<TResult>`:

```csharp
public interface IQuery<TResult>;
```

**Example -- get a product by ID:**

```csharp
public sealed record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDto>;
```

**Example -- list products with pagination:**

```csharp
public sealed record ListProductsQuery(
    int Page,
    int PageSize,
    string? SearchTerm) : IQuery<PagedResult<ProductDto>>;
```

## Handlers

Every command and query needs a corresponding handler. Handlers contain the actual business logic.

### ICommandHandler\<TCommand\>

Handles commands that return `Result` (no value):

```csharp
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}
```

**Example:**

```csharp
public sealed class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand>
{
    private readonly IProductRepository _repository;

    public DeleteProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(
        DeleteProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await _repository.GetByIdAsync(command.ProductId, cancellationToken);

        if (product is null)
        {
            return Error.NotFound(
                "Product.NotFound",
                $"Product with ID {command.ProductId} was not found.");
        }

        await _repository.DeleteAsync(product, cancellationToken);

        return Result.Success();
    }
}
```

### ICommandHandler\<TCommand, TResult\>

Handles commands that return `Result<TResult>`:

```csharp
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> Handle(TCommand command, CancellationToken cancellationToken);
}
```

**Example:**

```csharp
public sealed class CreateProductCommandHandler
    : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _repository;

    public CreateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Guid>> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var existingProduct = await _repository.GetBySkuAsync(command.Sku, cancellationToken);

        if (existingProduct is not null)
        {
            return Error.Conflict(
                "Product.DuplicateSku",
                $"A product with SKU '{command.Sku}' already exists.");
        }

        var product = new Product(command.Name, command.Price, command.Sku);

        await _repository.AddAsync(product, cancellationToken);

        return product.Id; // implicit conversion to Result<Guid>
    }
}
```

### IQueryHandler\<TQuery, TResult\>

Handles queries that return `Result<TResult>`:

```csharp
public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> Handle(TQuery query, CancellationToken cancellationToken);
}
```

**Example:**

```csharp
public sealed class GetProductByIdQueryHandler
    : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    private readonly IProductRepository _repository;

    public GetProductByIdQueryHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<ProductDto>> Handle(
        GetProductByIdQuery query,
        CancellationToken cancellationToken)
    {
        var product = await _repository.GetByIdAsync(query.ProductId, cancellationToken);

        if (product is null)
        {
            return Error.NotFound(
                "Product.NotFound",
                $"Product with ID {query.ProductId} was not found.");
        }

        return new ProductDto(product.Id, product.Name, product.Price, product.Sku);
    }
}
```

## Dispatching via IMediator

Inject `IMediator` and call the appropriate method:

```csharp
public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products");

        // Command with return value
        group.MapPost("/", async (
            CreateProductCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);

            return result.Match(
                onSuccess: id => Results.Created($"/products/{id}", id),
                onFailure: errors => Results.BadRequest(errors));
        });

        // Query
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Query(new GetProductByIdQuery(id), ct);

            return result.Match(
                onSuccess: product => Results.Ok(product),
                onFailure: errors => Results.NotFound(errors));
        });

        // Command with no return value
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteProductCommand(id), ct);

            return result.Match(
                onSuccess: () => Results.NoContent(),
                onFailure: errors => Results.NotFound(errors));
        });
    }
}
```

## Handler Auto-Discovery

When you call `AddModulusMediator(assemblies)`, the mediator uses [Scrutor](https://github.com/khellang/Scrutor) to scan the provided assemblies and automatically register all implementations of:

- `ICommandHandler<TCommand>`
- `ICommandHandler<TCommand, TResult>`
- `IQueryHandler<TQuery, TResult>`
- `IStreamQueryHandler<TQuery, TResult>`
- `IDomainEventHandler<TEvent>`

Handlers are registered with **scoped** lifetime by default, so they participate in the same DI scope as your DbContext and other scoped services.

::: info No manual registration needed
You do not need to register handlers individually. Just ensure the assembly containing your handlers is passed to `AddModulusMediator()`. Scrutor finds and registers them automatically.
:::

## Best Practices

- **One handler per command/query.** Each command or query should have exactly one handler. The mediator will throw if it finds zero or multiple handlers for the same request type.
- **Use records for commands and queries.** Records give you immutability, value equality, and concise syntax. Use `sealed record` to prevent inheritance.
- **Keep handlers focused.** A handler should do one thing. If you find a handler growing large, consider splitting the work into smaller commands or extracting shared logic into domain services.
- **Return errors, do not throw.** Use `Result.Failure()` and `Error` factory methods instead of throwing exceptions for expected failure cases. Reserve exceptions for truly unexpected situations.
- **Use ICommand\<T\> sparingly.** Most commands can return `Result` (no value). Only use `ICommand<TResult>` when the caller genuinely needs a value back (e.g., a newly created entity ID).

## See Also

- [Result Pattern](./result-pattern) -- How `Result`, `Result<T>`, and `Error` work
- [Pipeline Behaviors](./pipeline-behaviors) -- Validation, logging, and custom middleware for commands and queries
- [Domain Events](./domain-events) -- In-process event publishing
- [Streaming Queries](./streaming) -- Streaming large result sets
