# Building Blocks Reference

Building blocks are the shared base classes and interfaces that every module in a Modulus solution depends on. They live in the `src/BuildingBlocks/` directory and are organized into three layers that mirror the module structure: Domain, Application, and Infrastructure.

```
src/BuildingBlocks/
├── Domain/           # Entity, AggregateRoot, ValueObject, domain event contracts
├── Application/      # UnitOfWork, Repository, Pagination
└── Infrastructure/   # BaseDbContext, EfRepository, module registration contracts
```

## BuildingBlocks.Domain

The Domain building blocks provide base types for modeling your domain. They have zero external dependencies.

### Entity\<TId\>

Base class for all domain entities. An entity has a unique identity and equality is determined by its `Id`, not by its property values.

```csharp
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);
    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        Equals(left, right);
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) =>
        !Equals(left, right);
}
```

Two entities are equal if and only if they have the same `Id`, regardless of the values of their other properties. This is a fundamental DDD principle -- identity defines an entity, not its attributes.

### AggregateRoot\<TId\>

Extends `Entity<TId>` to serve as the root of an aggregate. Aggregate roots are the only entities that can raise domain events and are the entry point for all state changes within the aggregate boundary.

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
```

::: tip Domain events lifecycle
Domain events are collected in the aggregate root's internal list. When `BaseDbContext.SaveChangesAsync()` is called, the context dispatches all pending domain events through the mediator before clearing them. This ensures events are published only after the state change is persisted.
:::

### ValueObject

Base class for value objects. Value objects have no identity -- equality is determined by comparing all their properties structurally.

```csharp
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return GetEqualityComponents()
            .SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Aggregate(0, (hash, component) =>
                HashCode.Combine(hash, component));
    }
}
```

**Example -- a `Money` value object:**

```csharp
public class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0) throw new DomainException("Amount cannot be negative.");
        if (string.IsNullOrWhiteSpace(currency)) throw new DomainException("Currency is required.");

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

### StronglyTypedId\<T\>

A base class for wrapping primitive ID types to provide type safety. Prevents accidentally passing a `Guid` that represents a `ProductId` where an `OrderId` is expected.

```csharp
public abstract class StronglyTypedId<T> : ValueObject
    where T : notnull
{
    public T Value { get; }

    protected StronglyTypedId(T value)
    {
        Value = value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString()!;
}
```

**Example:**

```csharp
public sealed class ProductId : StronglyTypedId<Guid>
{
    public ProductId(Guid value) : base(value) { }

    public static ProductId New() => new(Guid.NewGuid());
}
```

::: info Strongly typed IDs are optional
Modulus does not force you to use strongly typed IDs. You can use `Guid`, `int`, `long`, or any other type as your entity ID. Strongly typed IDs are a recipe for teams that want extra type safety. See [Strongly Typed IDs](/recipes/strongly-typed-ids) for EF Core configuration details.
:::

### IAuditable

Interface for entities that track creation and modification timestamps. The `AuditableEntityInterceptor` in Infrastructure automatically sets these properties on `SaveChanges`.

```csharp
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}
```

### DomainException

Base exception for domain invariant violations. Use this when a domain rule is broken and the operation cannot proceed.

```csharp
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

::: warning Use Result for expected failures
`DomainException` is for genuine invariant violations that indicate a programming error or an impossible state. For expected business failures (e.g., "product not found"), use the `Result` pattern instead. See [Result Pattern](/mediator/result-pattern).
:::

### IDomainEvent

Marker interface for domain events. Domain events represent something that happened within a single module and are dispatched in-process by the mediator.

```csharp
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
```

## BuildingBlocks.Application

The Application building blocks define abstractions for data access and common query patterns.

### IUnitOfWork

Abstraction for committing a batch of changes atomically. Implemented by `BaseDbContext`.

```csharp
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
```

Inject `IUnitOfWork` into command handlers to persist state changes:

```csharp
public async Task<Result<Guid>> Handle(
    CreateProduct command,
    CancellationToken cancellationToken)
{
    var product = Product.Create(command.Name, command.Price);
    await _repository.AddAsync(product, cancellationToken);
    await _unitOfWork.CommitAsync(cancellationToken);
    return Result<Guid>.Success(product.Id);
}
```

### IRepository\<T\>

Generic repository interface for aggregate root persistence.

```csharp
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        where TId : notnull;
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Delete(T entity);
}
```

::: tip Custom repository interfaces
For queries that go beyond basic CRUD, define a custom repository interface in the Application layer (e.g., `IProductRepository`) and implement it in Infrastructure. The generic `IRepository<T>` covers the common cases.
:::

### PaginationQuery & PagedResult\<T\>

Standardized types for paginated queries and results.

```csharp
public abstract record PaginationQuery(int PageNumber = 1, int PageSize = 20);
```

```csharp
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
```

**Example -- paginated query:**

```csharp
public sealed record ListProducts(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null) : PaginationQuery(PageNumber, PageSize), IQuery<PagedResult<ProductDto>>;
```

## BuildingBlocks.Infrastructure

The Infrastructure building blocks provide concrete implementations and shared infrastructure plumbing.

### BaseDbContext

Abstract DbContext that implements `IUnitOfWork` and dispatches domain events on `SaveChangesAsync`. All module DbContexts extend this class.

```csharp
public abstract class BaseDbContext : DbContext, IUnitOfWork
{
    private readonly IMediator _mediator;

    protected BaseDbContext(DbContextOptions options, IMediator mediator)
        : base(options)
    {
        _mediator = mediator;
    }

    public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
    {
        // Dispatch domain events before saving
        await DispatchDomainEventsAsync(cancellationToken);

        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var aggregateRoots = ChangeTracker.Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = aggregateRoots
            .SelectMany(a => a.DomainEvents)
            .ToList();

        aggregateRoots.ForEach(a => a.ClearDomainEvents());

        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }
}
```

::: info Events before save
Domain events are dispatched **before** `SaveChangesAsync` is called. This means event handlers can make additional changes within the same transaction. If any handler fails, the entire save operation is rolled back.
:::

### EfRepository\<T\>

Generic EF Core repository implementation that satisfies `IRepository<T>`.

```csharp
public class EfRepository<T> : IRepository<T> where T : class
{
    protected readonly DbContext Context;
    protected readonly DbSet<T> DbSet;

    public EfRepository(DbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync<TId>(
        TId id, CancellationToken cancellationToken = default)
        where TId : notnull
    {
        return await DbSet.FindAsync([id], cancellationToken);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
    }

    public void Update(T entity) => DbSet.Update(entity);
    public void Delete(T entity) => DbSet.Remove(entity);
}
```

### AuditableEntityInterceptor

An EF Core `SaveChangesInterceptor` that automatically sets `CreatedAt` and `UpdatedAt` properties on entities implementing `IAuditable`.

```csharp
public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
```

Register the interceptor when configuring the module's DbContext:

```csharp
services.AddDbContext<CatalogDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
    options.AddInterceptors(new AuditableEntityInterceptor());
});
```

### IModuleRegistration

The contract that every module must implement to register its services and endpoints with the host application.

```csharp
public interface IModuleRegistration
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    void ConfigureEndpoints(IEndpointRouteBuilder app);
}
```

The host's `Program.cs` discovers all `IModuleRegistration` implementations at startup and invokes them in sequence. See [Module Anatomy](./module-anatomy) for the full registration pattern.

### IEndpoint

Interface for individual endpoint definitions. Each endpoint class maps a single HTTP route.

```csharp
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
```

The module registration class discovers all `IEndpoint` implementations in the module's assembly and calls `MapEndpoint` during startup.

### Outbox and Inbox EF Configurations

The Infrastructure building blocks include EF Core entity type configurations for the transactional outbox and inbox tables. These are applied automatically when a module uses `Modulus.Messaging`.

```csharp
// Applied in BaseDbContext.OnModelCreating
modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
```

The outbox stores integration events that need to be published to the message bus. The inbox tracks consumed events to ensure idempotent processing. See [Outbox Pattern](/messaging/outbox-pattern) and [Inbox Pattern](/messaging/inbox-pattern) for details.

## Summary

| Building Block | Layer | Purpose |
|---|---|---|
| `Entity<TId>` | Domain | Base entity with identity-based equality |
| `AggregateRoot<TId>` | Domain | Entity that can raise domain events |
| `ValueObject` | Domain | Structural equality, no identity |
| `StronglyTypedId<T>` | Domain | Type-safe ID wrapper |
| `IAuditable` | Domain | CreatedAt / UpdatedAt tracking |
| `DomainException` | Domain | Domain invariant violation |
| `IDomainEvent` | Domain | In-process domain event marker |
| `IUnitOfWork` | Application | Atomic commit abstraction |
| `IRepository<T>` | Application | Generic CRUD repository |
| `PaginationQuery` | Application | Paginated query base record |
| `PagedResult<T>` | Application | Paginated result container |
| `BaseDbContext` | Infrastructure | DbContext with UnitOfWork and event dispatch |
| `EfRepository<T>` | Infrastructure | Generic EF Core repository |
| `AuditableEntityInterceptor` | Infrastructure | Auto-sets audit timestamps |
| `IModuleRegistration` | Infrastructure | Module DI and endpoint registration |
| `IEndpoint` | Infrastructure | Single endpoint definition |

## See Also

- [Module Anatomy](./module-anatomy) -- How modules use these building blocks
- [Mediator](/mediator/) -- CQRS dispatch and domain event publishing
- [Strongly Typed IDs](/recipes/strongly-typed-ids) -- EF Core configuration for strongly typed IDs
