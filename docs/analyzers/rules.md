# Rule Reference

## MOD001: Module Boundary Violation {#mod001}

| Property | Value |
|---|---|
| **Severity** | Error |
| **Category** | Architecture |
| **Code Fix** | No |

### Description

Modules must not reference other modules' Domain, Application, Infrastructure, or Api namespaces. The only cross-module reference allowed is to another module's **Integration** project, which contains shared event contracts.

### Why This Matters

Module isolation is the foundation of a modular monolith. If modules freely reference each other's internals, you lose the ability to extract modules into microservices and create tight coupling that makes the codebase fragile. Integration projects provide a narrow, stable contract surface between modules.

### Violation

```csharp
// In Orders.Application project
using EShop.Modules.Catalog.Domain.Products; // MOD001: boundary violation

public class PlaceOrderHandler : ICommandHandler<PlaceOrder>
{
    public async Task<Result> Handle(PlaceOrder command, CancellationToken ct)
    {
        var product = new Product(); // Direct reference to Catalog.Domain
        return Result.Success();
    }
}
```

### Correct Code

```csharp
// In Orders.Application project
using EShop.Modules.Catalog.Integration.Events; // OK: Integration is allowed

public class CatalogItemCreatedHandler : IIntegrationEventHandler<CatalogItemCreated>
{
    public async Task Handle(CatalogItemCreated @event, CancellationToken ct)
    {
        // React to integration event -- no direct domain reference
    }
}
```

### Allowed References

- **Same module**: `Orders.Domain` can reference `Orders.Application`
- **BuildingBlocks**: Any module can reference `BuildingBlocks.*`
- **Integration**: `Orders.Infrastructure` can reference `Catalog.Integration`

---

## MOD002: Handler Return Type {#mod002}

| Property | Value |
|---|---|
| **Severity** | Warning |
| **Category** | Convention |
| **Code Fix** | No |

### Description

Command and query handlers must return `Task<Result>` or `Task<Result<T>>`. This enforces the Result pattern across all handlers, ensuring error handling is explicit rather than exception-based.

### Why This Matters

The Result pattern makes error handling composable and explicit. When all handlers return `Result`, the pipeline behaviors (validation, logging, exception handling) can work uniformly. Returning raw types or `Task` bypasses this safety net.

### Violation

```csharp
public class GetProductHandler : IQueryHandler<GetProduct, ProductDto>
{
    public async Task<ProductDto> Handle(GetProduct query, CancellationToken ct) // MOD002
    {
        // Returns ProductDto directly instead of Result<ProductDto>
        return new ProductDto();
    }
}
```

### Correct Code

```csharp
public class GetProductHandler : IQueryHandler<GetProduct, ProductDto>
{
    public async Task<Result<ProductDto>> Handle(GetProduct query, CancellationToken ct)
    {
        var product = await _repository.GetByIdAsync(query.Id, ct);
        if (product is null)
            return Error.NotFound("Product.NotFound", "Product not found");

        return new ProductDto(product.Id, product.Name);
    }
}
```

---

## MOD003: Exception Throwing in Handlers {#mod003}

| Property | Value |
|---|---|
| **Severity** | Warning |
| **Category** | Convention |
| **Code Fix** | Yes -- converts `throw` to `return Error.*()` |

### Description

Handlers should return `Error` values through the Result pattern instead of throwing domain exceptions for expected error cases. This analyzer detects `throw` statements in handlers where the exception type contains keywords like "NotFound", "Validation", "Conflict", "Unauthorized", or "Forbidden".

### Why This Matters

Throwing exceptions for expected business errors (not found, validation failure, conflict) creates invisible control flow that bypasses the pipeline. The Result pattern makes error handling explicit and allows behaviors like `LoggingBehavior` to accurately report outcomes.

Exceptions excluded from this rule: `ArgumentNullException`, `ArgumentException`, `ArgumentOutOfRangeException`, `InvalidOperationException`, `NotImplementedException`, `NotSupportedException`, `ObjectDisposedException`, and generic `Exception`.

### Violation

```csharp
public class GetProductHandler : IQueryHandler<GetProduct, ProductDto>
{
    public async Task<Result<ProductDto>> Handle(GetProduct query, CancellationToken ct)
    {
        var product = await _repository.GetByIdAsync(query.Id, ct);
        if (product is null)
            throw new NotFoundException("Product not found"); // MOD003
        return new ProductDto(product.Id, product.Name);
    }
}
```

### Correct Code (after code fix)

```csharp
public class GetProductHandler : IQueryHandler<GetProduct, ProductDto>
{
    public async Task<Result<ProductDto>> Handle(GetProduct query, CancellationToken ct)
    {
        var product = await _repository.GetByIdAsync(query.Id, ct);
        if (product is null)
            return Error.NotFound("NotFoundException", "Product not found");
        return new ProductDto(product.Id, product.Name);
    }
}
```

### Code Fix

The code fix automatically transforms:

| Exception keyword | Generated Error method |
|---|---|
| NotFound | `Error.NotFound()` |
| Validation | `Error.Validation()` |
| Conflict | `Error.Conflict()` |
| Unauthorized | `Error.Unauthorized()` |
| Forbidden | `Error.Forbidden()` |

---

## MOD004: Domain Infrastructure Leak {#mod004}

| Property | Value |
|---|---|
| **Severity** | Warning |
| **Category** | Architecture |
| **Code Fix** | Yes -- removes the offending attribute or `using` directive |

### Description

Domain layer projects (assemblies ending in `.Domain`) should not contain infrastructure concerns. This analyzer detects:

- **Forbidden attributes**: `[Column]`, `[Table]`, `[Key]`, `[JsonPropertyName]`, `[JsonIgnore]`, and other EF Core / JSON serialization attributes
- **Forbidden using directives**: `Microsoft.EntityFrameworkCore`, `Newtonsoft.Json`

### Why This Matters

The Domain layer should be a pure model of your business rules with no knowledge of persistence or serialization technology. Infrastructure attributes in the Domain layer create coupling to specific frameworks and make the domain model harder to test and evolve independently.

### Violation

```csharp
// In Catalog.Domain project
using System.ComponentModel.DataAnnotations; // MOD004
using Microsoft.EntityFrameworkCore; // MOD004

namespace EShop.Modules.Catalog.Domain.Products;

public class Product : AggregateRoot<ProductId>
{
    [Required] // MOD004: infrastructure attribute in Domain
    [MaxLength(200)]
    public string Name { get; private set; }
}
```

### Correct Code

```csharp
// In Catalog.Domain project
namespace EShop.Modules.Catalog.Domain.Products;

public class Product : AggregateRoot<ProductId>
{
    public string Name { get; private set; }

    // Validation belongs in Application layer (FluentValidation)
    // EF constraints belong in Infrastructure layer (EntityTypeConfiguration)
}
```

---

## MOD005: Public Setter on Entity {#mod005}

| Property | Value |
|---|---|
| **Severity** | Info |
| **Category** | Convention |
| **Code Fix** | Yes -- changes `set` to `private set` |

### Description

Properties on types that inherit from `Entity` or `AggregateRoot` should use `private set` accessors. Public setters break encapsulation by allowing external code to modify entity state directly, bypassing domain logic and invariant checks.

### Why This Matters

In Domain-Driven Design, entities enforce their own invariants. State changes should go through methods that validate business rules. A public setter allows any caller to modify state without validation, leading to an inconsistent domain model.

### Violation

```csharp
public class Product : AggregateRoot<ProductId>
{
    public string Name { get; set; } // MOD005: public setter on entity
    public decimal Price { get; set; } // MOD005
}
```

### Correct Code (after code fix)

```csharp
public class Product : AggregateRoot<ProductId>
{
    public string Name { get; private set; }
    public decimal Price { get; private set; }

    public void UpdatePrice(decimal newPrice)
    {
        if (newPrice <= 0)
            throw new ArgumentException("Price must be positive", nameof(newPrice));
        Price = newPrice;
    }
}
```
