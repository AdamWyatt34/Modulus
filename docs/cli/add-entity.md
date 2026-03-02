# modulus add-entity

Scaffolds a domain entity or aggregate root inside a module's Domain layer. The generated entity includes a strongly-typed ID, optional properties, and follows Modulus domain conventions.

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
| `--properties, -p <props>` | Comma-separated `Name:Type` pairs to generate as properties (e.g., `"Name:string,Price:decimal"`). | No properties |

## Generated Output

Running `modulus add-entity Product --module Catalog --aggregate --properties "Name:string,Price:decimal"` generates:

### Entity file

`src/Modules/Catalog/EShop.Modules.Catalog.Domain/Entities/Product.cs`

```csharp
using EShop.SharedKernel.Domain;

namespace EShop.Modules.Catalog.Domain.Entities;

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

        return product;
    }
}
```

### Strongly-typed ID

`src/Modules/Catalog/EShop.Modules.Catalog.Domain/Entities/ProductId.cs`

```csharp
using EShop.SharedKernel.Domain;

namespace EShop.Modules.Catalog.Domain.Entities;

public sealed class ProductId : StronglyTypedId<Guid>
{
    public ProductId(Guid value) : base(value) { }

    public static ProductId New() => new(Guid.NewGuid());
}
```

### Other ID Types

When you specify a different `--id-type`, the generated ID type adapts accordingly:

```bash
# Integer ID
modulus add-entity Order --module Orders --id-type int

# String ID (e.g., for natural keys)
modulus add-entity Tenant --module Identity --id-type string

# Custom type
modulus add-entity Invoice --module Billing --id-type Ulid
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

## See Also

- [modulus add-command](./add-command) -- Create commands that operate on your entities
- [modulus add-query](./add-query) -- Create queries that return your entities
- [Building Blocks](/architecture/building-blocks) -- Entity and aggregate root conventions
- [Strongly Typed IDs](/recipes/strongly-typed-ids) -- Deep dive into the ID system
