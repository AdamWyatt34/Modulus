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
| `--dry-run` | Print the file that would be created (and the route-parameter binding it would need) without writing anything. | Disabled |

::: warning Mutual Exclusivity
The `--command` and `--query` options are mutually exclusive. An endpoint can be wired to a command **or** a query, but not both. If neither is specified, a bare endpoint stub is generated.
:::

## Generated Output

The command generates a single file: `src/Modules/{Module}/src/{Module}.Api/Endpoints/{EndpointName}.cs` -- an `IEndpoint` implementation. No other file changes are needed: the module's `{Module}EndpointRegistration` discovers every `IEndpoint` in the Api assembly by reflection and maps it inside the module's route group.

### Route parameters are bound automatically

Every `{param}` segment in `--route` becomes a leading parameter on the generated lambda, typed from its route constraint (`{id:guid}` → `Guid id`, `{page:int}` → `int page`, `{id:int?}` → `int? id`, a bare `{name}` with no constraint → `string name`), and is forwarded **positionally** into the wired command/query's constructor call, in the order the parameters appear in the route.

Running `modulus add-endpoint GetProduct --module Catalog --method GET --route "/{id:guid}" --query GetProductById --result-type ProductDto` generates:

```csharp
namespace EShop.Catalog.Api.Endpoints;

public sealed class GetProduct : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Query(new GetProductById(id), ct);
            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .WithName("GetProduct")
        .Produces<ProductDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}
```

::: warning The target record must declare matching positional parameters
`new GetProductById(id)` only compiles once `GetProductById` itself declares a matching positional
parameter, in the same order the route lists them, e.g.:

```csharp
public sealed record GetProductById(Guid Id) : IQuery<ProductDto>;
```

`modulus add-query`/`modulus add-command` scaffold a parameterless record by default (see their
own docs) -- add the positional parameter(s) yourself after scaffolding a route-bound endpoint.
Route parameter names must be unique within a route and cannot collide with the generated lambda's
own `mediator`/`ct` parameters; the command rejects both up front.
:::

For a `POST` command with a route parameter, the `Location` header on `201 Created` interpolates
the actual bound value rather than echoing the raw route template -- `--route "/items/{itemId}"`
produces `Results.Created($"/api/catalog/items/{itemId}", value)`, and a route constraint like
`{id:guid}` is stripped down to the bare `{id}` hole in that interpolation (the constraint itself
stays intact in the route template passed to `MapPost`/`MapGet`/etc.).

### Endpoint wired to a command

Running `modulus add-endpoint CreateProductEndpoint --module Catalog --method POST --route / --command CreateProduct --result-type Guid` generates `Endpoints/CreateProductEndpoint.cs` with the same shape, dispatching `mediator.Send(new CreateProduct(), ct)` and returning `201 Created` via `result.Match(...)` (or `204 No Content` when `--result-type` is omitted). Give the endpoint a different name than the command -- inside a class named `CreateProduct`, the generated `new CreateProduct()` would resolve to the endpoint class itself.

### Bare endpoint (no command or query)

When neither `--command` nor `--query` is specified, a minimal stub is generated that you can fill in manually:

```csharp
namespace EShop.Catalog.Api.Endpoints;

public sealed class HealthCheck : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (CancellationToken ct) =>
        {
            // TODO: Wire up to a command or query
            return Results.Ok();
        })
        .WithName("HealthCheck");
    }
}
```

### Route Registration

Nothing to register manually: `{Module}EndpointRegistration.Map{Module}Endpoints()` scans the Api assembly for `IEndpoint` implementations at startup and maps each one onto the module's route group, so the final route is the group prefix plus your `--route` (e.g. `/api/catalog/products`). The group itself is wired by `{Module}Module.ConfigureEndpoints`, which module auto-discovery invokes from the host.

## Examples

**Create a POST endpoint wired to a command:**

```bash
modulus add-endpoint CreateProductEndpoint --module Catalog --method POST --route / --command CreateProduct --result-type Guid
```

**Create a GET endpoint wired to a query:**

```bash
modulus add-endpoint GetProduct --module Catalog --method GET --route "/{id:guid}" --query GetProductById --result-type ProductDto
```

**Create a DELETE endpoint wired to a command:**

```bash
modulus add-endpoint CancelOrderEndpoint --module Orders --method DELETE --route "/{id:guid}" --command CancelOrder
```

**Create a bare endpoint stub:**

```bash
modulus add-endpoint HealthCheck --module Catalog --method GET --route /health
```

**Create an endpoint with multiple route parameters (bound in order):**

```bash
modulus add-endpoint GetItem --module Catalog --method GET --route "/{parentId:guid}/items/{itemId:guid}" --query GetItem --result-type ItemDto
```

**Preview the file that would be created without writing anything:**

```bash
modulus add-endpoint GetProduct --module Catalog --method GET --route "/{id:guid}" --query GetProductById --result-type ProductDto --dry-run
```

## See Also

- [modulus add-command](./add-command) -- Create commands to wire to POST/PUT/DELETE endpoints
- [modulus add-query](./add-query) -- Create queries to wire to GET endpoints
- [modulus add-module](./add-module) -- The Api layer where endpoints live
