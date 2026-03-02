# modulus add-endpoint

Scaffolds a minimal API endpoint inside a module's Api layer. Endpoints can be wired directly to an existing command or query, generating the full request-to-response pipeline in one step.

## Synopsis

```bash
modulus add-endpoint <endpoint-name> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `<endpoint-name>` | PascalCase name for the endpoint (e.g., `CreateProduct`, `GetOrderById`). |

## Options

| Option | Description | Default |
|---|---|---|
| `--module, -m <name>` | **(Required)** Target module where the endpoint will be created. | -- |
| `--method <method>` | HTTP method: `GET`, `POST`, `PUT`, or `DELETE`. | `GET` |
| `--route <template>` | Route template relative to the module's route group (e.g., `/`, `/{id:guid}`, `/{id}/items`). | `/` |
| `--command <name>` | Wire the endpoint to an existing command. Mutually exclusive with `--query`. | -- |
| `--query <name>` | Wire the endpoint to an existing query. Mutually exclusive with `--command`. | -- |
| `--result-type, -r <type>` | Result type for the wired command or query. Required when using `--command` or `--query`. | -- |
| `--solution, -s <path>` | Path to the `.slnx` solution file. | Auto-discovered |

::: warning Mutual Exclusivity
The `--command` and `--query` options are mutually exclusive. An endpoint can be wired to a command **or** a query, but not both. If neither is specified, a bare endpoint stub is generated.
:::

## Generated Output

### Endpoint wired to a command

Running `modulus add-endpoint CreateProduct --module Catalog --method POST --route / --command CreateProduct --result-type Guid` generates:

`src/Modules/Catalog/EShop.Modules.Catalog.Api/Endpoints/CreateProductEndpoint.cs`

```csharp
using EShop.Modules.Catalog.Application.Commands.CreateProduct;
using EShop.SharedKernel.Application;

namespace EShop.Modules.Catalog.Api.Endpoints;

public static class CreateProductEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            CreateProductCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await mediator.Send(command, cancellationToken);

            return result.IsSuccess
                ? Results.Created($"/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });
    }
}
```

### Endpoint wired to a query

Running `modulus add-endpoint GetProduct --module Catalog --method GET --route "/{id:guid}" --query GetProductById --result-type ProductDto` generates:

`src/Modules/Catalog/EShop.Modules.Catalog.Api/Endpoints/GetProductEndpoint.cs`

```csharp
using EShop.Modules.Catalog.Application.Queries.GetProductById;
using EShop.SharedKernel.Application;

namespace EShop.Modules.Catalog.Api.Endpoints;

public static class GetProductEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var query = new GetProductByIdQuery { Id = id };
            Result<ProductDto> result = await mediator.Send(query, cancellationToken);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound(result.Error);
        });
    }
}
```

### Bare endpoint (no command or query)

When neither `--command` nor `--query` is specified, a minimal stub is generated that you can fill in manually:

```csharp
namespace EShop.Modules.Catalog.Api.Endpoints;

public static class HealthCheckEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/health", () =>
        {
            // TODO: Implement endpoint logic
            return Results.Ok();
        });
    }
}
```

### Route Registration

The generated endpoint is automatically registered in the module's `CatalogModule.cs` file, which maps all endpoints under the module's route group prefix (e.g., `/api/catalog`).

## Examples

**Create a POST endpoint wired to a command:**

```bash
modulus add-endpoint CreateProduct --module Catalog --method POST --route / --command CreateProduct --result-type Guid
```

**Create a GET endpoint wired to a query:**

```bash
modulus add-endpoint GetProduct --module Catalog --method GET --route "/{id:guid}" --query GetProductById --result-type ProductDto
```

**Create a DELETE endpoint wired to a command:**

```bash
modulus add-endpoint CancelOrder --module Orders --method DELETE --route "/{id:guid}" --command CancelOrder
```

**Create a bare endpoint stub:**

```bash
modulus add-endpoint HealthCheck --module Catalog --method GET --route /health
```

## See Also

- [modulus add-command](./add-command) -- Create commands to wire to POST/PUT/DELETE endpoints
- [modulus add-query](./add-query) -- Create queries to wire to GET endpoints
- [modulus add-module](./add-module) -- The Api layer where endpoints live
