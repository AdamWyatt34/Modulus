# Building Blocks Reference

Building blocks are the shared base classes and interfaces that every module in a Modulus solution depends on. They are four projects under `src/` (grouped in a `BuildingBlocks` solution folder), mirroring the module layer structure -- namespaces are `{Solution}.BuildingBlocks.{Layer}.*`:

```
src/
├── BuildingBlocks.Domain/           # Entity, AggregateRoot, ValueObject, StronglyTypedId, domain event contracts
├── BuildingBlocks.Application/      # IRepository, Pagination
├── BuildingBlocks.Infrastructure/   # BaseDbContext, EfRepository, IModuleRegistration, IEndpoint, outbox/inbox configs
└── BuildingBlocks.Integration/      # IIntegrationEvent re-export
```

## BuildingBlocks.Domain

The Domain building blocks provide base types for modeling your domain. They have zero external dependencies.

### Entity\<TId\>

Base class for all domain entities. An entity has a unique identity and equality is determined by its `Id`, not by its property values.

```csharp
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; protected init; } = default!;

    public bool Equals(Entity<TId>? other)
    {
        if (other is null || other.GetType() != GetType())
        {
            return false;
        }

        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
```

Two entities are equal if and only if they are the same concrete type and have the same `Id`, regardless of the values of their other properties. This is a fundamental DDD principle -- identity defines an entity, not its attributes.

### AggregateRoot\<TId\>

Extends `Entity<TId>` to serve as the root of an aggregate. Aggregate roots are the only entities that can raise domain events and are the entry point for all state changes within the aggregate boundary.

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

::: tip Domain events lifecycle
Domain events are collected in the aggregate root's internal list. `BaseDbContext.SaveChangesAsync()` extracts and clears them from all tracked aggregates, saves the changes, and **then** dispatches the events through the mediator. This ensures events are published only after the state change is persisted.
:::

### ValueObject

Base class for value objects. Value objects have no identity -- equality is determined by comparing all their properties structurally.

```csharp
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null || other.GetType() != GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Aggregate(0, (hash, component) => HashCode.Combine(hash, component));
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);
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

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

### StronglyTypedId\<T\>

A Guid-backed abstract record for wrapping entity IDs to provide type safety. Prevents accidentally passing a `Guid` that represents a `ProductId` where an `OrderId` is expected. The type parameter is the derived ID type itself (a self-referencing constraint), which gives every ID `IComparable<T>` for free; record semantics provide value equality.

```csharp
public abstract record StronglyTypedId<T>(Guid Value)
    : IComparable<T>
    where T : StronglyTypedId<T>
{
    public override string ToString() => Value.ToString();

    public int CompareTo(T? other) => other is null ? 1 : Value.CompareTo(other.Value);
}
```

**Example (this is exactly what `modulus add-entity --id-type ProductId` generates):**

```csharp
public sealed record ProductId(Guid Value) : StronglyTypedId<ProductId>(Value)
{
    public static ProductId New() => new(Guid.NewGuid());
}
```

::: info Strongly typed IDs are optional
Modulus does not force you to use strongly typed IDs. You can use `Guid`, `int`, `long`, or `string` as your entity ID. This record-based base is the scaffold's zero-dependency option; the `[StronglyTypedId]` attribute from `ModulusKit.Generators` is the source-generated alternative with EF Core/JSON/route-binding converters built in. See [Strongly Typed IDs](/recipes/strongly-typed-ids) for the trade-offs and EF Core configuration details.
:::

### IAuditable

Interface for entities that track creation and modification timestamps. The `AuditableEntityInterceptor` in Infrastructure sets these properties on `SaveChanges` once it is attached to your DbContext (see [below](#auditableentityinterceptor)).

```csharp
public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }

    DateTime? UpdatedAtUtc { get; set; }
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

### IDomainEvent and DomainEvent

`IDomainEvent` comes from `Modulus.Mediator.Abstractions` -- `BuildingBlocks.Domain` re-exports it (via a `global using`) so modules do not need a direct package reference. Domain events represent something that happened within a single module and are dispatched in-process by the mediator.

```csharp
public interface IDomainEvent
{
    Guid Id { get; }
    DateTime OccurredOnUtc { get; }
}
```

`BuildingBlocks.Domain` also ships a base record with the boilerplate filled in:

```csharp
public abstract record DomainEvent : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
```

## BuildingBlocks.Application

The Application building blocks define abstractions for data access and common query patterns.

### IUnitOfWork

Abstraction for committing a batch of changes atomically. This interface lives in `Modulus.Mediator.Abstractions` (not in BuildingBlocks) and is implemented by `BaseDbContext`; the library's opt-in `UnitOfWorkBehavior` calls it automatically after successful commands.

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

Inject `IUnitOfWork` into command handlers to persist state changes:

```csharp
public async Task<Result<Guid>> Handle(
    CreateProduct command,
    CancellationToken cancellationToken = default)
{
    var product = Product.Create(Guid.NewGuid(), command.Name, command.Price);
    await _repository.AddAsync(product, cancellationToken);
    await _unitOfWork.SaveChangesAsync(cancellationToken);
    return Result<Guid>.Success(product.Id);
}
```

### IRepository\<T, TId\>

Generic repository interface for aggregate root persistence, in `BuildingBlocks.Application.Persistence`.

```csharp
public interface IRepository<T, in TId>
    where T : AggregateRoot<TId>
    where TId : notnull
{
    Task<T?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ListAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    void Update(T entity);

    void Remove(T entity);
}
```

::: tip Custom repository interfaces
For queries that go beyond basic CRUD, define a per-aggregate repository interface (e.g., `IProductRepository` -- this is what `modulus add-entity` generates, in the Domain layer so Domain keeps zero outward dependencies) and implement it in Infrastructure. The generic `IRepository<T, TId>` covers the common cases.
:::

### PaginationQuery & PagedResult\<T\>

Standardized types for paginated queries and results, in `BuildingBlocks.Application.Pagination`.

```csharp
public abstract record PaginationQuery
{
    private const int MaxPageSize = 100;

    public int Page { get; init; } = 1;

    private int _pageSize = 10;

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value > MaxPageSize ? MaxPageSize : value < 1 ? 1 : value;
    }
}
```

```csharp
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}
```

**Example -- paginated query:**

```csharp
public sealed record ListProducts : PaginationQuery, IQuery<PagedResult<ProductDto>>
{
    public string? SearchTerm { get; init; }
}
```

## BuildingBlocks.Infrastructure

The Infrastructure building blocks provide concrete implementations and shared infrastructure plumbing.

### BaseDbContext

Abstract DbContext that implements `IUnitOfWork` and dispatches domain events after `SaveChangesAsync`. All module DbContexts extend this class. It also maps the outbox and inbox tables into every module context (see [below](#outbox-and-inbox-ef-configurations)).

```csharp
public abstract class BaseDbContext : DbContext, IUnitOfWork
{
    private readonly IMediator _mediator;

    protected BaseDbContext(DbContextOptions options, IMediator mediator) : base(options)
    {
        _mediator = mediator;
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<OutboxMessageConsumer> OutboxMessageConsumers => Set<OutboxMessageConsumer>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<InboxMessageConsumer> InboxMessageConsumers => Set<InboxMessageConsumer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
        modelBuilder.ApplyConfiguration(new InboxConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var domainEvents = ExtractDomainEvents(); // collects + clears from tracked aggregates

        int result = await base.SaveChangesAsync(cancellationToken);

        await DispatchDomainEvents(domainEvents, cancellationToken); // mediator.Publish per event

        return result;
    }
}
```

::: info Events after save
Domain events are dispatched **after** `base.SaveChangesAsync()` completes -- handlers observe committed state, and a failed save means no events are published. The flip side: work an event handler does is *not* part of the original save. Handlers that must write additional rows call `SaveChangesAsync` themselves (the scaffolded `IdempotentDomainEventHandler` decorator adds at-most-once tracking for exactly this pattern).
:::

### EfRepository\<T, TId\>

Generic EF Core repository implementation that satisfies `IRepository<T, TId>`.

```csharp
public class EfRepository<T, TId>(DbContext context) : IRepository<T, TId>
    where T : AggregateRoot<TId>
    where TId : notnull
{
    protected DbContext Context => context;

    protected DbSet<T> DbSet => context.Set<T>();

    public virtual async Task<T?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<IReadOnlyList<T>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
    }

    public virtual void Update(T entity) => DbSet.Update(entity);

    public virtual void Remove(T entity) => DbSet.Remove(entity);
}
```

### AuditableEntityInterceptor

An EF Core `SaveChangesInterceptor` (sync and async) that sets `CreatedAtUtc` on added entities and `UpdatedAtUtc` on added/modified entities implementing `IAuditable`:

```csharp
public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
    // SavingChanges / SavingChangesAsync both call:
    private static void UpdateAuditableEntities(DbContext context)
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = utcNow;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = utcNow;
            }
        }
    }
}
```

The interceptor only runs on contexts it is attached to -- add it when configuring the module's DbContext in `{Module}Module.ConfigureServices`:

```csharp
services.AddDbContext<CatalogDbContext>((sp, options) =>
{
    options.UseSqlServer(configuration.GetConnectionString("Default"));
    options.AddInterceptors(new AuditableEntityInterceptor());
});
```

### IModuleRegistration

The contract that every module implements to register its services and endpoints with the host application. Its members are `static abstract`, so a module type is wired without ever being instantiated:

```csharp
public interface IModuleRegistration
{
    static abstract IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration);

    static abstract IEndpointRouteBuilder ConfigureEndpoints(IEndpointRouteBuilder endpoints);
}
```

The module auto-discovery source generator finds all `IModuleRegistration` implementations in the host's referenced assemblies and emits `AddAllModules` / `MapAllModuleEndpoints`, which the host's `Program.cs` calls. See [Module Anatomy](./module-anatomy) for the full registration pattern.

### IEndpoint

Interface for individual endpoint definitions. Each endpoint class maps a single HTTP route.

```csharp
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
```

The module's `{Module}EndpointRegistration` class (in the Api layer) discovers all `IEndpoint` implementations in the module's assembly by reflection and calls `MapEndpoint` on each, inside the module's `/api/{module}` route group.

### Outbox and Inbox EF Configurations

The Infrastructure building blocks include EF Core entity type configurations for the transactional outbox and inbox tables -- `OutboxConfiguration`, `OutboxMessageConsumerConfiguration`, `InboxConfiguration`, and `InboxMessageConsumerConfiguration` -- applied by `BaseDbContext.OnModelCreating`, so every module DbContext maps these tables:

```csharp
// Applied in BaseDbContext.OnModelCreating
modelBuilder.ApplyConfiguration(new OutboxConfiguration());
modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
modelBuilder.ApplyConfiguration(new InboxConfiguration());
modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());
```

The outbox table stores integration events to be published; having it mapped in your module context is what enables the [same-transaction outbox configuration](/messaging/outbox-pattern#transactionality-the-two-configurations). The `OutboxMessageConsumer` table backs the scaffolded `IdempotentDomainEventHandler` decorator (at-most-once domain event handling), and the inbox tables track consumed integration events for idempotent processing. See [Outbox Pattern](/messaging/outbox-pattern) and [Inbox Pattern](/messaging/inbox-pattern) for details.

## Summary

| Building Block | Project | Purpose |
|---|---|---|
| `Entity<TId>` | BuildingBlocks.Domain | Base entity with identity-based equality |
| `AggregateRoot<TId>` | BuildingBlocks.Domain | Entity that can raise domain events |
| `ValueObject` | BuildingBlocks.Domain | Structural equality, no identity |
| `StronglyTypedId<T>` | BuildingBlocks.Domain | Guid-backed type-safe ID record |
| `IAuditable` | BuildingBlocks.Domain | CreatedAtUtc / UpdatedAtUtc tracking |
| `DomainException` | BuildingBlocks.Domain | Domain invariant violation |
| `IDomainEvent` / `DomainEvent` | BuildingBlocks.Domain (re-export) | In-process domain event contract + base record |
| `IUnitOfWork` | Modulus.Mediator.Abstractions | Atomic commit abstraction (`SaveChangesAsync`) |
| `IRepository<T, TId>` | BuildingBlocks.Application | Generic CRUD repository contract |
| `PaginationQuery` | BuildingBlocks.Application | Paginated query base record |
| `PagedResult<T>` | BuildingBlocks.Application | Paginated result container |
| `BaseDbContext` | BuildingBlocks.Infrastructure | DbContext with UnitOfWork, outbox/inbox tables, post-save event dispatch |
| `EfRepository<T, TId>` | BuildingBlocks.Infrastructure | Generic EF Core repository |
| `AuditableEntityInterceptor` | BuildingBlocks.Infrastructure | Sets audit timestamps (attach to your DbContext) |
| `IdempotentDomainEventHandler<T>` | BuildingBlocks.Infrastructure | At-most-once decorator for domain event handlers |
| `IModuleRegistration` | BuildingBlocks.Infrastructure | Static-abstract module DI and endpoint registration |
| `IEndpoint` / `ApiResults` | BuildingBlocks.Infrastructure | Single endpoint definition + Result-to-HTTP problem mapping |

## See Also

- [Module Anatomy](./module-anatomy) -- How modules use these building blocks
- [Mediator](/mediator/) -- CQRS dispatch and domain event publishing
- [Strongly Typed IDs](/recipes/strongly-typed-ids) -- EF Core configuration for strongly typed IDs
