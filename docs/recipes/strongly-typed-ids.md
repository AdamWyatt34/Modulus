# Strongly Typed IDs

## Problem

Using primitive types like `Guid` for entity identifiers leads to primitive obsession -- a `Guid` representing a `ProductId` is interchangeable with a `Guid` representing an `OrderId`, and the compiler cannot tell them apart. This makes it easy to accidentally pass the wrong ID to the wrong method, and the error only surfaces at runtime.

```csharp
// Compiles fine, but is a bug -- passing a ProductId where an OrderId is expected
var order = await _orderRepository.GetByIdAsync(productId, ct);
```

## Solution

Use the `StronglyTypedId<T>` base class from BuildingBlocks to create type-safe ID wrappers. Each entity gets its own ID type, and the compiler enforces correct usage at every call site.

### Step 1: Define the Strongly Typed ID

In the Domain layer of your module:

```csharp
namespace EShop.Modules.Catalog.Domain.Products;

public sealed class ProductId : StronglyTypedId<Guid>
{
    public ProductId(Guid value) : base(value) { }

    public static ProductId New() => new(Guid.NewGuid());
}
```

```csharp
namespace EShop.Modules.Orders.Domain.Orders;

public sealed class OrderId : StronglyTypedId<Guid>
{
    public OrderId(Guid value) : base(value) { }

    public static OrderId New() => new(Guid.NewGuid());
}
```

Now the compiler catches mistakes:

```csharp
// Compiler error: cannot convert from ProductId to OrderId
var order = await _orderRepository.GetByIdAsync(productId, ct);
```

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

### Step 3: Use with the CLI

The Modulus CLI supports strongly typed IDs when adding entities:

```bash
modulus add-entity Product --module Catalog --id-type ProductId
```

This generates the entity with the correct `AggregateRoot<ProductId>` base class and creates the `ProductId` class in the Domain layer.

### Step 4: Configure EF Core Value Converter

EF Core needs to know how to convert between the strongly typed ID and the underlying database type. Create a value converter and apply it in the entity configuration:

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EShop.Modules.Catalog.Infrastructure.Data.Configurations;

public class ProductIdConverter : ValueConverter<ProductId, Guid>
{
    public ProductIdConverter()
        : base(
            id => id.Value,       // to database
            value => new ProductId(value))  // from database
    { }
}
```

Apply the converter in the entity type configuration:

```csharp
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion<ProductIdConverter>();

        builder.Property(p => p.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Price)
            .HasPrecision(18, 2);
    }
}
```

::: tip Generic converter
If you have many strongly typed IDs, create a generic converter to reduce boilerplate:

```csharp
public class StronglyTypedIdConverter<TId> : ValueConverter<TId, Guid>
    where TId : StronglyTypedId<Guid>
{
    public StronglyTypedIdConverter()
        : base(
            id => id.Value,
            value => (TId)Activator.CreateInstance(typeof(TId), value)!)
    { }
}
```

Then use it as:

```csharp
builder.Property(p => p.Id)
    .HasConversion<StronglyTypedIdConverter<ProductId>>();
```
:::

### Step 5: Configure JSON Serialization

For minimal API endpoints to correctly serialize and deserialize strongly typed IDs in request/response bodies and route parameters, configure a JSON converter:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public class StronglyTypedIdJsonConverter<TId> : JsonConverter<TId>
    where TId : StronglyTypedId<Guid>
{
    public override TId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetGuid();
        return (TId)Activator.CreateInstance(typeof(TId), value)!;
    }

    public override void Write(
        Utf8JsonWriter writer,
        TId value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
```

Register the converter in `Program.cs`:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new StronglyTypedIdJsonConverter<ProductId>());
    options.SerializerOptions.Converters.Add(
        new StronglyTypedIdJsonConverter<OrderId>());
});
```

### Step 6: Use in Endpoints

With the JSON converter registered, endpoints work seamlessly with strongly typed IDs:

```csharp
public class GetProductEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/catalog/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var productId = new ProductId(id);
            var result = await mediator.Query(
                new GetProductByIdQuery(productId), ct);

            return result.Match(
                onSuccess: product => Results.Ok(product),
                onFailure: errors => Results.NotFound(errors));
        });
    }
}
```

### Step 7: Use in Commands and Queries

```csharp
public sealed record GetProductByIdQuery(ProductId ProductId)
    : IQuery<ProductDto>;

public sealed record CreateProductCommand(
    string Name,
    decimal Price) : ICommand<ProductId>;
```

## Discussion

Strongly typed IDs add a small amount of boilerplate (the ID class, the EF converter, the JSON converter) but provide significant safety benefits:

- **Compile-time safety** -- The compiler rejects incorrect ID usage before the code runs.
- **Self-documenting code** -- Method signatures clearly communicate which entity they operate on: `GetByIdAsync(ProductId id)` is unambiguous.
- **Refactoring confidence** -- Changing an ID type propagates through the entire codebase via compiler errors, ensuring nothing is missed.

The `StronglyTypedId<T>` base class from BuildingBlocks extends `ValueObject`, which means two IDs with the same underlying value are considered equal. This aligns with the value object semantics expected for identifiers.

::: info Strongly typed IDs are optional
Modulus does not require strongly typed IDs. If your team prefers plain `Guid` identifiers, the entire framework works with `Entity<Guid>` and `AggregateRoot<Guid>`. Adopt strongly typed IDs when the type safety benefits outweigh the additional boilerplate for your project.
:::

## See Also

- [Building Blocks](/architecture/building-blocks) -- `StronglyTypedId<T>`, `Entity<TId>`, and `ValueObject` base classes
- [CLI: add-entity](/cli/add-entity) -- The `--id-type` flag for scaffolding entities with strongly typed IDs
