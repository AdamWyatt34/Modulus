# Streaming Queries

Streaming queries let you return large or unbounded result sets as an `IAsyncEnumerable<T>` instead of loading everything into memory at once. This is useful for scenarios like exporting data, real-time feeds, or processing large collections efficiently.

## IStreamQuery Interface

Streaming queries implement the `IStreamQuery<TResult>` marker interface:

```csharp
public interface IStreamQuery<TResult>;
```

**Example -- stream all products:**

```csharp
public sealed record StreamAllProductsQuery(
    string? Category) : IStreamQuery<ProductDto>;
```

**Example -- stream audit log entries:**

```csharp
public sealed record StreamAuditLogQuery(
    DateTime FromUtc,
    DateTime ToUtc) : IStreamQuery<AuditLogEntry>;
```

## IStreamQueryHandler Interface

Streaming query handlers implement `IStreamQueryHandler<TQuery, TResult>` and return `IAsyncEnumerable<TResult>`:

```csharp
public interface IStreamQueryHandler<in TQuery, out TResult>
    where TQuery : IStreamQuery<TResult>
{
    IAsyncEnumerable<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}
```

Note that the return type is `IAsyncEnumerable<TResult>`, not `Task<Result<IAsyncEnumerable<TResult>>>`. Streaming queries yield items one at a time and do not use the `Result` wrapper.

## Defining a Streaming Handler

### Example: Stream Products from a Database

```csharp
public sealed class StreamAllProductsQueryHandler
    : IStreamQueryHandler<StreamAllProductsQuery, ProductDto>
{
    private readonly CatalogDbContext _dbContext;

    public StreamAllProductsQueryHandler(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async IAsyncEnumerable<ProductDto> Handle(
        StreamAllProductsQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var productsQuery = _dbContext.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            productsQuery = productsQuery.Where(p => p.Category == query.Category);
        }

        await foreach (var product in productsQuery.AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            yield return new ProductDto(
                product.Id,
                product.Name,
                product.Price,
                product.Sku);
        }
    }
}
```

### Example: Stream Audit Log with Date Filtering

```csharp
public sealed class StreamAuditLogQueryHandler
    : IStreamQueryHandler<StreamAuditLogQuery, AuditLogEntry>
{
    private readonly IAuditLogRepository _repository;

    public StreamAuditLogQueryHandler(IAuditLogRepository repository)
    {
        _repository = repository;
    }

    public async IAsyncEnumerable<AuditLogEntry> Handle(
        StreamAuditLogQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in _repository
            .GetEntriesBetweenAsync(query.FromUtc, query.ToUtc)
            .WithCancellation(cancellationToken))
        {
            yield return entry;
        }
    }
}
```

## Dispatching Streaming Queries

Use `IMediator.Stream()` to dispatch a streaming query:

```csharp
IAsyncEnumerable<ProductDto> products = mediator.Stream(
    new StreamAllProductsQuery(Category: "Electronics"),
    cancellationToken);
```

### Consuming with await foreach

```csharp
await foreach (var product in mediator.Stream(
    new StreamAllProductsQuery(Category: null), ct))
{
    Console.WriteLine($"{product.Name}: {product.Price:C}");
}
```

### In a Minimal API Endpoint

Streaming queries work naturally with ASP.NET Core's streaming response support:

```csharp
app.MapGet("/products/export", (
    string? category,
    IMediator mediator,
    CancellationToken ct) =>
{
    var products = mediator.Stream(
        new StreamAllProductsQuery(category), ct);

    return Results.Ok(products);
});
```

### Collecting Results When Needed

If you need the full list in memory (for smaller result sets), you can materialize the stream:

```csharp
var allProducts = new List<ProductDto>();

await foreach (var product in mediator.Stream(
    new StreamAllProductsQuery(Category: null), ct))
{
    allProducts.Add(product);
}

// Or use System.Linq.Async:
var allProducts = await mediator
    .Stream(new StreamAllProductsQuery(Category: null), ct)
    .ToListAsync(ct);
```

## Pipeline Behaviors Are Not Applied

::: warning Important limitation
Pipeline behaviors are **not** applied to streaming queries. The `IPipelineBehavior<TRequest, TResponse>` interface expects handlers to return `Task<TResponse>`, which is incompatible with `IAsyncEnumerable<TResult>`.

This means:
- **No ValidationBehavior** -- Validation is not automatically run before streaming handlers
- **No LoggingBehavior** -- Start/duration/outcome is not logged
- **No UnhandledExceptionBehavior** -- Exceptions propagate directly to the caller
- **No MetricsBehavior** -- Handler duration is not recorded
:::

If you need these cross-cutting concerns for streaming queries, implement them directly in your handler:

```csharp
public async IAsyncEnumerable<ProductDto> Handle(
    StreamAllProductsQuery query,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    // Manual validation
    if (query.Category is not null && query.Category.Length > 100)
    {
        throw new ArgumentException("Category filter is too long.");
    }

    // Manual logging
    _logger.LogInformation("Streaming products for category: {Category}", query.Category);

    var count = 0;
    var stopwatch = Stopwatch.StartNew();

    await foreach (var product in _dbContext.Products.AsNoTracking()
        .AsAsyncEnumerable()
        .WithCancellation(cancellationToken))
    {
        count++;
        yield return new ProductDto(product.Id, product.Name, product.Price, product.Sku);
    }

    _logger.LogInformation("Streamed {Count} products in {Elapsed}ms",
        count, stopwatch.ElapsedMilliseconds);
}
```

## When to Use Streaming vs Regular Queries

| Scenario | Use |
|---|---|
| Fetch a single entity by ID | `IQuery<T>` |
| Fetch a paginated list (known size) | `IQuery<PagedResult<T>>` |
| Export thousands of records | `IStreamQuery<T>` |
| Real-time data feed or cursor-based iteration | `IStreamQuery<T>` |
| Result needs validation pipeline | `IQuery<T>` |
| Result needs to be wrapped in `Result<T>` | `IQuery<T>` |
| Low memory footprint for large datasets | `IStreamQuery<T>` |

::: info Rule of thumb
Use `IQuery<T>` when the result set fits comfortably in memory and you want the full pipeline (validation, logging, metrics). Use `IStreamQuery<T>` when you need to process items one at a time, the dataset is large, or you want to start processing before the query completes.
:::

## Handler Auto-Discovery

Like all other handler types, streaming query handlers are auto-discovered by Scrutor when you call `AddModulusMediator(assemblies)`. You do not need to register them manually.

## See Also

- [Commands & Queries](./commands-queries) -- Standard request types with Result pattern
- [Pipeline Behaviors](./pipeline-behaviors) -- Pipeline middleware (does not apply to streaming)
