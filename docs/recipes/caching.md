# Adding Caching

## Problem

Frequently executed queries hit the database on every request, adding latency and load. You want to cache query results so that repeated reads are served from memory or a distributed cache instead of the database.

## Solution

Add `IDistributedCache` to your host, create a `CachingBehavior<TRequest, TResponse>` pipeline behavior, and mark cacheable queries with an `ICacheable` marker interface.

### Step 1: Install the Caching Package

For Redis-based distributed caching:

```bash
dotnet add src/EShop.Host/ package Microsoft.Extensions.Caching.StackExchangeRedis
```

Or for in-memory caching (useful for development and single-instance deployments):

```bash
dotnet add src/EShop.Host/ package Microsoft.Extensions.Caching.Memory
```

### Step 2: Configure the Cache

In the host's `Program.cs`:

```csharp
// Redis (production)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "EShop:";
});
```

Or for development:

```csharp
// In-memory (development / testing)
builder.Services.AddDistributedMemoryCache();
```

### Step 3: Define the ICacheable Interface

Place this in your BuildingBlocks.Application project so all modules can use it:

```csharp
namespace EShop.BuildingBlocks.Application;

public interface ICacheable
{
    string CacheKey { get; }
    TimeSpan? CacheDuration { get; }
}
```

### Step 4: Create the Caching Behavior

```csharp
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace EShop.BuildingBlocks.Infrastructure;

public sealed class CachingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICacheable
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(
        IDistributedCache cache,
        ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Try to get from cache
        var cachedValue = await _cache.GetStringAsync(
            request.CacheKey, cancellationToken);

        if (cachedValue is not null)
        {
            _logger.LogDebug("Cache hit for {CacheKey}", request.CacheKey);

            return JsonSerializer.Deserialize<TResponse>(cachedValue)!;
        }

        // Cache miss -- execute the handler
        _logger.LogDebug("Cache miss for {CacheKey}", request.CacheKey);

        var result = await next();

        // Cache the result
        var duration = request.CacheDuration ?? TimeSpan.FromMinutes(5);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = duration
        };

        await _cache.SetStringAsync(
            request.CacheKey,
            JsonSerializer.Serialize(result),
            options,
            cancellationToken);

        return result;
    }
}
```

### Step 5: Register the Behavior

```csharp
services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
services.AddPipelineBehavior(typeof(CachingBehavior<,>));   // After validation
services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
```

### Step 6: Mark Queries as Cacheable

Apply the `ICacheable` interface to any query that should be cached:

```csharp
public sealed record GetProductByIdQuery(Guid ProductId)
    : IQuery<ProductDto>, ICacheable
{
    public string CacheKey => $"products:{ProductId}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(10);
}
```

```csharp
public sealed record ListProductsQuery(
    int Page,
    int PageSize,
    string? SearchTerm)
    : IQuery<PagedResult<ProductDto>>, ICacheable
{
    public string CacheKey => $"products:list:{Page}:{PageSize}:{SearchTerm ?? "all"}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(2);
}
```

Queries that do not implement `ICacheable` are unaffected -- the `where TRequest : ICacheable` constraint ensures the behavior only applies to cacheable queries.

### Step 7: Invalidate Cache on Writes

When a command modifies data, invalidate the relevant cache entries. You can do this in the command handler:

```csharp
public sealed class CreateProductHandler : ICommandHandler<CreateProduct, Guid>
{
    private readonly IRepository<Product> _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDistributedCache _cache;

    public CreateProductHandler(
        IRepository<Product> repository,
        IUnitOfWork unitOfWork,
        IDistributedCache cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Result<Guid>> Handle(
        CreateProduct command,
        CancellationToken cancellationToken)
    {
        var product = Product.Create(command.Name, command.Price);
        await _repository.AddAsync(product, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        // Invalidate list cache
        await _cache.RemoveAsync("products:list:*", cancellationToken);

        return product.Id;
    }
}
```

::: tip Cache invalidation strategies
The example above uses explicit key removal. For more sophisticated scenarios, consider:
- **Tag-based invalidation** -- Group related cache entries with tags and invalidate by tag.
- **Event-driven invalidation** -- Use a domain event handler to invalidate cache entries when entities change.
- **Short TTLs** -- For data that changes frequently, use short cache durations (30 seconds to 2 minutes) and accept eventual consistency.
:::

## Discussion

The `CachingBehavior` is a pure cross-cutting concern -- it wraps the handler without modifying the handler's logic. This means you can add caching to any query by implementing `ICacheable` on the query record. No handler changes are required.

The behavior only applies to types that implement `ICacheable`, so non-cacheable queries pass through the pipeline without any caching overhead. This opt-in approach ensures you only cache what makes sense for your application.

Place the `CachingBehavior` after the `ValidationBehavior` in the pipeline. This ensures invalid requests are rejected before the cache is consulted, and cache entries are never populated with validation error responses.

## See Also

- [Pipeline Behaviors](/mediator/pipeline-behaviors) -- How pipeline behaviors work
- [Commands & Queries](/mediator/commands-queries) -- The query types that get cached
