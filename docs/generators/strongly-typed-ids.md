# Strongly Typed IDs

The Strongly Typed ID generator transforms a `readonly partial record struct` annotated with `[StronglyTypedId]` into a complete value type with EF Core persistence, JSON serialization, and minimal API model binding support.

## Quick Start

```csharp
using Modulus.Mediator.Abstractions;

namespace EShop.Modules.Catalog.Domain.Products;

[StronglyTypedId]
public readonly partial record struct ProductId;
```

This single declaration generates all the infrastructure code you need to use `ProductId` throughout your application.

## What Gets Generated

For each annotated type, the generator produces a `{TypeName}.g.cs` file containing:

| Generated member | Purpose |
|---|---|
| `Value` property | The underlying backing value (e.g., `Guid`) |
| Constructor | Creates an instance from the backing value |
| `New()` static method | Creates a new instance with a random value (Guid-backed only) |
| `Empty` static property | The default/empty value |
| `ToString()` override | Returns the string representation of the value |
| `{TypeName}ValueConverter` | EF Core `ValueConverter<TId, TBacking>` for database persistence |
| `{TypeName}JsonConverter` | System.Text.Json `JsonConverter<TId>` for API serialization |
| `{TypeName}TypeConverter` | System.ComponentModel `TypeConverter` for route parameter binding |

## Supported Backing Types

| Backing Type | Attribute Usage | `New()` | `Empty` Value |
|---|---|---|---|
| `Guid` (default) | `[StronglyTypedId]` | `Guid.NewGuid()` | `Guid.Empty` |
| `int` | `[StronglyTypedId(typeof(int))]` | Not generated | `0` |
| `long` | `[StronglyTypedId(typeof(long))]` | Not generated | `0L` |

```csharp
// Guid-backed (default)
[StronglyTypedId]
public readonly partial record struct OrderId;

// int-backed
[StronglyTypedId(typeof(int))]
public readonly partial record struct SequenceNumber;

// long-backed
[StronglyTypedId(typeof(long))]
public readonly partial record struct EventOffset;
```

## EF Core Integration

Use the generated value converter in your entity type configuration:

```csharp
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion<ProductIdValueConverter>();
    }
}
```

The `ProductIdValueConverter` is generated automatically -- no manual converter class needed. It converts between `ProductId` and the backing type (`Guid`, `int`, or `long`) for database storage.

## JSON Serialization

The generated `ProductIdJsonConverter` handles serialization and deserialization in System.Text.Json. Guid-backed IDs serialize as strings, while `int` and `long` IDs serialize as numbers.

Register the converter globally or use it with `[JsonConverter]`:

```csharp
// Global registration
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new ProductIdJsonConverter());
});

// Or per-type
[JsonConverter(typeof(ProductIdJsonConverter))]
public readonly partial record struct ProductId;
```

## Minimal API Model Binding

The generated `TypeConverter` enables automatic route parameter binding. Minimal API endpoints can accept strongly typed IDs directly:

```csharp
app.MapGet("/products/{id}", async (ProductId id, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Query(new GetProductByIdQuery(id), ct);
    return result.Match(
        onSuccess: product => Results.Ok(product),
        onFailure: errors => Results.NotFound(errors));
});
```

The `TypeConverter` parses the route string into the backing type and constructs the strongly typed ID automatically.

## Complete Flow

The full lifecycle of a strongly typed ID:

```
1. [StronglyTypedId] attribute on record struct
                ↓
2. Source generator produces ValueConverter, JsonConverter, TypeConverter
                ↓
3. EF Core uses ValueConverter to persist Guid ↔ ProductId
                ↓
4. Handler creates/queries entities using ProductId
                ↓
5. JSON response uses JsonConverter to serialize ProductId
                ↓
6. Incoming requests use TypeConverter to bind route parameters
```

## Generator Diagnostics

| ID | Severity | Message |
|---|---|---|
| MODGEN001 | Error | `[StronglyTypedId]` requires the `partial` modifier |
| MODGEN002 | Error | `[StronglyTypedId]` requires a `record struct` declaration |

If you see MODGEN001, add the `partial` keyword. If you see MODGEN002, change your type from a `class` or `struct` to a `record struct`.

## See Also

- [Strongly Typed IDs Recipe](/recipes/strongly-typed-ids) -- Step-by-step guide for using strongly typed IDs in a module
- [Handler Registration](./handler-registration) -- Auto-register handlers that use strongly typed IDs
- [Result Pattern](/mediator/result-pattern) -- Combine strongly typed IDs with the Result pattern
