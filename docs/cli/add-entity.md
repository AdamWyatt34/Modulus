# modulus add-entity

Scaffolds a domain entity or aggregate root inside a module's Domain layer, together with its repository interface and EF Core plumbing. The ID can be a built-in type (`guid`, `int`, `long`, `string`) or a custom strongly-typed ID record that the command generates for you.

## Synopsis

```bash
modulus add-entity <entity-name> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `<entity-name>` | PascalCase name for the entity (e.g., `Product`, `ShoppingCart`). |

## Options

| Option | Description | Default |
|---|---|---|
| `--module, -m <name>` | **(Required)** Target module where the entity will be created. | -- |
| `--solution, -s <path>` | Path to the `.slnx` solution file. | Auto-discovered |
| `--aggregate` | Generate the entity as an `AggregateRoot` instead of a plain `Entity`. Aggregate roots can raise domain events and serve as consistency boundaries. | Plain `Entity` |
| `--id-type <type>` | Type for the strongly-typed ID: `guid`, `int`, `long`, `string`, or any custom type. | `guid` |
| `--properties, -p <props>` | Comma-separated `Name:Type` pairs to generate as properties (e.g., `"Name:string,Price:decimal"`). Types accept built-in aliases, BCL types, fully-qualified names, nullable (`string?`), arrays (`decimal[]`), and generic types (`List<string>`, `Dictionary<string, decimal>`, arbitrarily nested) -- commas nested inside `<...>` are not treated as property separators. | No properties |
| `--dry-run` | Print the files that would be created without writing anything. | Disabled |

## Generated Output

Running `modulus add-entity Product --module Catalog --aggregate --properties "Name:string,Price:decimal"` generates five files under `src/Modules/Catalog/`:

| File | Purpose |
|---|---|
| `src/Catalog.Domain/Entities/Product.cs` | The entity/aggregate class |
| `src/Catalog.Domain/Repositories/IProductRepository.cs` | Repository interface (self-contained, Domain has no outward dependencies) |
| `src/Catalog.Infrastructure/Persistence/Repositories/ProductRepository.cs` | EF Core repository (aggregates inherit `EfRepository<,>`) |
| `src/Catalog.Infrastructure/Persistence/Configurations/ProductConfiguration.cs` | EF Core entity type configuration (auto-discovered by the DbContext) |
| `tests/Catalog.Tests.Unit/Domain/ProductTests.cs` | Starter unit test for the factory method |

### Entity file

`src/Modules/Catalog/src/Catalog.Domain/Entities/Product.cs`

```csharp
using EShop.BuildingBlocks.Domain.Entities;

namespace EShop.Catalog.Domain.Entities;

public class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; } = default!;
    public decimal Price { get; private set; } = default!;

    private Product() { }

    public static Product Create(Guid id, string name, decimal price)
    {
        return new Product { Id = id, Name = name, Price = price };
    }
}
```

### Custom Strongly-typed ID

Passing a custom type name to `--id-type` (anything other than `guid`, `int`, `long`, `string`) additionally generates a Guid-backed ID record built on the scaffolded `StronglyTypedId<T>` base:

`src/Modules/Catalog/src/Catalog.Domain/Identifiers/ProductId.cs`

```csharp
using EShop.BuildingBlocks.Domain.Identifiers;

namespace EShop.Catalog.Domain.Identifiers;

public sealed record ProductId(Guid Value) : StronglyTypedId<ProductId>(Value)
{
    public static ProductId New() => new(Guid.NewGuid());
}
```

The entity then uses `AggregateRoot<ProductId>`, and the generated `ProductConfiguration` adds the `HasConversion` mapping for it.

### Other ID Types

```bash
# Integer ID
modulus add-entity Order --module Orders --id-type int

# String ID (e.g., for natural keys)
modulus add-entity Tenant --module Identity --id-type string

# Custom strongly-typed ID (generates ProductId-style record)
modulus add-entity Invoice --module Billing --id-type InvoiceId
```

## Examples

**Create a simple entity:**

```bash
modulus add-entity Product --module Catalog
```

**Create an aggregate root with properties:**

```bash
modulus add-entity Order --module Orders --aggregate --properties "CustomerId:Guid,Total:decimal,Status:OrderStatus"
```

**Create an entity with an integer ID:**

```bash
modulus add-entity Category --module Catalog --id-type int --properties "Name:string,Description:string"
```

**Create an aggregate root with a string ID:**

```bash
modulus add-entity Tenant --module Identity --aggregate --id-type string --properties "Name:string,Subdomain:string"
```

**Create an entity with a generic property type:**

```bash
modulus add-entity Product --module Catalog --properties "Name:string,Tags:List<string>,Prices:Dictionary<string,decimal>"
```

**Preview the files that would be created without writing anything:**

```bash
modulus add-entity Product --module Catalog --properties "Name:string,Price:decimal" --dry-run
```

## See Also

- [modulus add-command](./add-command) -- Create commands that operate on your entities
- [modulus add-query](./add-query) -- Create queries that return your entities
- [Building Blocks](/architecture/building-blocks) -- Entity and aggregate root conventions
- [Strongly Typed IDs](/recipes/strongly-typed-ids) -- Deep dive into the ID system
