# modulus add-query

Scaffolds a CQRS query and its handler inside a module's Application layer. Queries represent read operations and always return a result. They are dispatched through the Modulus mediator pipeline and should have no side effects.

## Synopsis

```bash
modulus add-query <query-name> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `<query-name>` | PascalCase name for the query (e.g., `GetProductById`, `ListOrders`). |

## Options

| Option | Description | Default |
|---|---|---|
| `--module, -m <name>` | **(Required)** Target module where the query will be created. | -- |
| `--result-type, -r <type>` | **(Required)** The return type wrapped in `Result<T>`. Every query must declare what it returns. Accepts built-in aliases, BCL types, fully-qualified names, nullable, arrays, and generic types (e.g. `ProductDto`, `List<ProductDto>`, `IReadOnlyList<ProductDto>`, `PagedResult<OrderSummaryDto>`). | -- |
| `--solution, -s <path>` | Path to the `.slnx` solution file. | Auto-discovered |
| `--dry-run` | Print the files that would be created without writing anything. | Disabled |

## Generated Output

Running `modulus add-query GetProductById --module Catalog --result-type ProductDto` generates three files -- the query record and handler under `src/Catalog.Application/Queries/GetProductById/`, plus a starter unit test in `tests/Catalog.Tests.Unit/Queries/`. The record and handler are named exactly after the query (no `Query` suffix is appended):

### Query record

`src/Modules/Catalog/src/Catalog.Application/Queries/GetProductById/GetProductById.cs`

```csharp
using Modulus.Mediator.Abstractions;

namespace EShop.Catalog.Application.Queries.GetProductById;

public sealed record GetProductById : IQuery<ProductDto>;
```

### Handler class

`src/Modules/Catalog/src/Catalog.Application/Queries/GetProductById/GetProductByIdHandler.cs`

```csharp
using Modulus.Mediator.Abstractions;

namespace EShop.Catalog.Application.Queries.GetProductById;

public sealed class GetProductByIdHandler : IQueryHandler<GetProductById, ProductDto>
{
    public Task<Result<ProductDto>> Handle(GetProductById query, CancellationToken cancellationToken = default)
    {
        // TODO: Implement query logic
        throw new NotImplementedException();
    }
}
```

The result type itself (`ProductDto` here) is not generated -- define it in the Application layer.

::: tip No Validator Generated
Unlike commands, queries do not generate a validator class. Queries are read-only operations and typically do not require input validation beyond what the type system provides. If you need validation on a query, you can add a validator manually and it will be picked up by the validation pipeline behavior automatically.
:::

## Examples

**Create a query returning a single DTO:**

```bash
modulus add-query GetProductById --module Catalog --result-type ProductDto
```

**Create a query returning a generic collection directly:**

```bash
modulus add-query ListProducts --module Catalog --result-type "List<ProductDto>"
```

**Create a query returning a paginated result:**

```bash
modulus add-query SearchOrders --module Orders --result-type "PagedResult<OrderSummaryDto>"
```

**Create a query with an explicit solution path:**

```bash
modulus add-query GetCustomerProfile --module Identity --result-type CustomerProfileDto --solution ./EShop.slnx
```

**Preview the files that would be created without writing anything:**

```bash
modulus add-query GetProductById --module Catalog --result-type ProductDto --dry-run
```

## See Also

- [modulus add-command](./add-command) -- Scaffold write-side commands
- [modulus add-endpoint](./add-endpoint) -- Wire queries to HTTP GET endpoints
- [Commands & Queries](/mediator/commands-queries) -- How the mediator dispatches queries
- [Streaming Queries](/mediator/streaming) -- For queries that return `IAsyncEnumerable<T>`
