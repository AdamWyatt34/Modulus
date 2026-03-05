# Strongly Typed IDs

## Problem

Using primitive types like `Guid` for entity identifiers leads to primitive obsession -- a `Guid` representing a `ProductId` is interchangeable with a `Guid` representing an `OrderId`, and the compiler cannot tell them apart. This makes it easy to accidentally pass the wrong ID to the wrong method, and the error only surfaces at runtime.

```csharp
// Compiles fine, but is a bug -- passing a ProductId where an OrderId is expected
var order = await _orderRepository.GetByIdAsync(productId, ct);
```

## Solution

Use the `[StronglyTypedId]` attribute from `Modulus.Mediator.Abstractions` to generate type-safe ID wrappers at compile time. Each entity gets its own ID type, and the compiler enforces correct usage at every call site.

### Step 1: Define the Strongly Typed ID

In the Domain layer of your module, define a `readonly partial record struct` with the `[StronglyTypedId]` attribute:

```csharp
using Modulus.Mediator.Abstractions;

namespace EShop.Modules.Catalog.Domain.Products;

[StronglyTypedId]
public readonly partial record struct ProductId;
```

```csharp
namespace EShop.Modules.Orders.Domain.Orders;

[StronglyTypedId]
public readonly partial record struct OrderId;
```

The source generator automatically produces:
- `Value` property and constructor
- `New()` static factory method (Guid-backed only)
- `Empty` static property
- `ProductIdValueConverter` for EF Core
- `ProductIdJsonConverter` for System.Text.Json
- `ProductIdTypeConverter` for minimal API model binding

Now the compiler catches mistakes:

```csharp
// Compiler error: cannot convert from ProductId to OrderId
var order = await _orderRepository.GetByIdAsync(productId, ct);
```

::: tip Non-Guid backing types
For integer-based IDs, pass the backing type to the attribute:

```csharp
[StronglyTypedId(typeof(int))]
public readonly partial record struct SequenceNumber;

[StronglyTypedId(typeof(long))]
public readonly partial record struct EventOffset;
```

Supported backing types: `Guid` (default), `int`, `long`.
:::

### Step 2: Use with Entities

Reference the strongly typed ID as the generic argument to `Entity<TId>` or `AggregateRoot<TId>`:

```csharp
public class Product : AggregateRoot<ProductId>
{
    public string Name { get; private set; }
    public decimal Price { get; private set; }

    private Product() { } // EF Core

    public static Product Create(string name, decimal price)
    {
        var product = new Product
        {
            Id = ProductId.New(),
            Name = name,
            Price = price
        };

        product.RaiseDomainEvent(new ProductCreatedEvent(product.Id));
        return product;
    }
}
```

### Step 3: Configure EF Core

Use the auto-generated value converter in your entity type configuration:

```csharp
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion<ProductIdValueConverter>();

        builder.Property(p => p.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Price)
            .HasPrecision(18, 2);
    }
}
```

No manual converter class needed -- `ProductIdValueConverter` is source-generated.

### Step 4: Use in Endpoints

The auto-generated `TypeConverter` enables automatic route parameter binding in minimal APIs:

```csharp
public class GetProductEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/catalog/{id}", async (
            ProductId id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Query(
                new GetProductByIdQuery(id), ct);

            return result.Match(
                onSuccess: product => Results.Ok(product),
                onFailure: errors => Results.NotFound(errors));
        });
    }
}
```

The `TypeConverter` handles parsing the route parameter string directly into a `ProductId` -- no manual `Guid` parsing required.

### Step 5: Use in Commands and Queries

```csharp
public sealed record GetProductByIdQuery(ProductId ProductId)
    : IQuery<ProductDto>;

public sealed record CreateProductCommand(
    string Name,
    decimal Price) : ICommand<ProductId>;
```

## Discussion

With the source generator, strongly typed IDs require **zero boilerplate** -- just one attribute on a `readonly partial record struct`. The generator produces all the infrastructure code:

- **Compile-time safety** -- The compiler rejects incorrect ID usage before the code runs.
- **Self-documenting code** -- Method signatures clearly communicate which entity they operate on: `GetByIdAsync(ProductId id)` is unambiguous.
- **Refactoring confidence** -- Changing an ID type propagates through the entire codebase via compiler errors, ensuring nothing is missed.
- **Full integration** -- EF Core persistence, JSON serialization, and API model binding all work automatically.

::: info Strongly typed IDs are optional
Modulus does not require strongly typed IDs. If your team prefers plain `Guid` identifiers, the entire framework works with `Entity<Guid>` and `AggregateRoot<Guid>`. Adopt strongly typed IDs when the type safety benefits matter for your project.
:::

## See Also

- [Source Generators: Strongly Typed IDs](/generators/strongly-typed-ids) -- Full generator reference with all options and diagnostics
- [Building Blocks](/architecture/building-blocks) -- `Entity<TId>` and `AggregateRoot<TId>` base classes
- [CLI: add-entity](/cli/add-entity) -- The `--id-type` flag for scaffolding entities with strongly typed IDs
