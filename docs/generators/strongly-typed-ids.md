# Strongly Typed IDs

The Strongly Typed ID generator transforms a `readonly partial record struct` annotated with `[StronglyTypedId]` into a complete value type with EF Core persistence, JSON serialization (including as a dictionary key), comparison, and minimal API route/query parameter binding.

## Quick Start

```csharp
using Modulus.Mediator.Abstractions;

namespace EShop.Catalog.Domain.Products;

[StronglyTypedId]
public readonly partial record struct ProductId;
```

This single declaration generates all the infrastructure code you need to use `ProductId` throughout your application.

## What Gets Generated

For each annotated type, the generator produces a `{TypeName}.g.cs` file containing:

| Generated member | Purpose |
|---|---|
| `Value` property | The underlying backing value (e.g., `Guid`) |
| Constructor | Creates an instance from the backing value (`string`-backed: throws `ArgumentNullException` on `null`) |
| `New()` static method | Creates a new instance with a random value (`Guid`-backed only) |
| `Empty` static property | The default/empty value |
| `ToString()` override | Returns the string representation of the value |
| `IComparable<TId>` | `CompareTo` delegates to the backing value's own comparison (`string`: ordinal via `string.CompareOrdinal`) |
| `IParsable<TId>` / `Parse` / `TryParse` | Enables minimal API route and query parameter binding |
| `{TypeName}ValueConverter` | EF Core `ValueConverter<TId, TBacking>` for database persistence |
| `{TypeName}JsonConverter` | System.Text.Json `JsonConverter<TId>` for API serialization -- values *and* `Dictionary<TId, TValue>` keys |
| `{TypeName}TypeConverter` | System.ComponentModel `TypeConverter` for MVC model binding |

## Supported Backing Types

| Backing Type | Attribute Usage | `New()` | `Empty` Value |
|---|---|---|---|
| `Guid` (default) | `[StronglyTypedId]` | `Guid.NewGuid()` | `Guid.Empty` |
| `int` | `[StronglyTypedId(typeof(int))]` | Not generated | `0` |
| `long` | `[StronglyTypedId(typeof(long))]` | Not generated | `0L` |
| `string` | `[StronglyTypedId(typeof(string))]` | Not generated (no natural generator) | `string.Empty` |

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

// string-backed -- useful for externally-issued identifiers (tenant slugs, external system keys)
[StronglyTypedId(typeof(string))]
public readonly partial record struct TenantId;
```

`string`-backed IDs have no natural "new random value" the way `Guid`/`int`/`long` do, so no `New()` factory is generated. The constructor rejects `null` (`ArgumentNullException`); `default(TenantId)` still bypasses the constructor entirely (as with any struct), so `ToString()` and `CompareTo` null-coalesce defensively instead of throwing.

## Comparison

Every strongly typed ID implements `IComparable<TId>`, so it can be sorted or used with `Comparer<TId>.Default` without any extra code:

```csharp
var ids = new List<ProductId> { new(5), new(1), new(3) };
ids.Sort(); // uses the generated CompareTo
```

## Minimal API Route and Query Parameter Binding

The generator emits `IParsable<TId>` (interface declared when the target framework defines `System.IParsable<T>`, i.e. .NET 7+) together with the static `Parse`/`TryParse` methods it requires. ASP.NET Core's minimal API binding discovers `TryParse` by signature convention, so a strongly typed ID binds directly from a route or query value with no extra code:

```csharp
app.MapGet("/products/{id}", async (ProductId id, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Query(new GetProductByIdQuery(id), ct);
    return result.Match(
        onSuccess: product => Results.Ok(product),
        onFailure: errors => Results.NotFound(errors));
});
```

`Parse`/`TryParse` always use `CultureInfo.InvariantCulture` for `int`/`long`/`Guid` backing types (ignoring the `IFormatProvider` argument) -- an ID's textual round-trip should not depend on the current thread's culture. The MVC-only `TypeConverter` remains for MVC/Razor Pages model binding, which does not use `IParsable<T>`.

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

The `ProductIdValueConverter` is generated automatically -- no manual converter class needed. It converts between `ProductId` and the backing type (`Guid`, `int`, `long`, or `string`) for database storage.

### Bulk registration via `ConfigureConventions`

Wiring `HasConversion<TIdValueConverter>()` for every ID on every entity gets repetitive fast. When the compilation references EF Core, the generator also emits a single helper -- `ModulusStronglyTypedIdConventions.UseModulusStronglyTypedIds(this ModelConfigurationBuilder)` -- that registers every `[StronglyTypedId]` discovered in the compilation, plus any public ones from referenced assemblies that were themselves built with an EF Core reference:

```csharp
public class CatalogDbContext : DbContext
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.UseModulusStronglyTypedIds();
}
```

This one call is equivalent to writing `configurationBuilder.Properties<ProductId>().HaveConversion<ProductId.ProductIdValueConverter>();` for every ID in scope -- it registers the conversion for *every* property of that CLR type, on every entity, without per-entity `HasConversion` calls. Like the per-ID `ValueConverter`, the helper (and the whole file it lives in) is only emitted when the compilation references EF Core, so a Domain project without an EF Core reference never sees `ModelConfigurationBuilder` at all.

## JSON Serialization

The generated `ProductIdJsonConverter` handles serialization and deserialization in System.Text.Json. Guid-backed and string-backed IDs serialize as strings, while `int` and `long` IDs serialize as numbers.

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

### Dictionary keys

The generated `JsonConverter` also overrides `ReadAsPropertyName`/`WriteAsPropertyName`, so a strongly typed ID works as a `Dictionary<TId, TValue>` key -- without this override, System.Text.Json throws `NotSupportedException` for any non-primitive key type:

```csharp
var stockByProduct = new Dictionary<ProductId, int>
{
    [new ProductId(Guid.Parse("11111111-1111-1111-1111-111111111111"))] = 42
};

var json = JsonSerializer.Serialize(stockByProduct);
// {"11111111-1111-1111-1111-111111111111":42}

var roundTripped = JsonSerializer.Deserialize<Dictionary<ProductId, int>>(json);
```

Numeric backing types serialize the property name as an invariant-culture string (e.g. `"42"`), parsed back the same way.

## Complete Flow

The full lifecycle of a strongly typed ID:

```
1. [StronglyTypedId] attribute on record struct
                ↓
2. Source generator produces ValueConverter, JsonConverter, TypeConverter, IComparable<T>, IParsable<T>
                ↓
3. EF Core uses ValueConverter (directly, or via UseModulusStronglyTypedIds()) to persist Guid ↔ ProductId
                ↓
4. Handler creates/queries entities using ProductId
                ↓
5. JSON response uses JsonConverter to serialize ProductId (as a value or a dictionary key)
                ↓
6. Incoming requests use TryParse (minimal APIs) or TypeConverter (MVC) to bind route/query parameters
```

## Generator Diagnostics

| ID | Severity | Message |
|---|---|---|
| MODGEN001 | Error | `[StronglyTypedId]` requires the `partial` modifier |
| MODGEN002 | Error | `[StronglyTypedId]` requires a `record struct` declaration |
| MODGEN005 | Error | `[StronglyTypedId]` backing type is unsupported (only `Guid`, `int`, `long`, `string`) |
| MODGEN006 | Error | `[StronglyTypedId]` target must be a top-level type |

If you see MODGEN001, add the `partial` keyword. If you see MODGEN002, change your type from a `class` or `struct` to a `record struct`. If you see MODGEN005, switch to one of the four supported backing types.

## See Also

- [Strongly Typed IDs Recipe](/recipes/strongly-typed-ids) -- Step-by-step guide for using strongly typed IDs in a module
- [Handler Registration](./handler-registration) -- Auto-register handlers that use strongly typed IDs
- [Result Pattern](/mediator/result-pattern) -- Combine strongly typed IDs with the Result pattern
