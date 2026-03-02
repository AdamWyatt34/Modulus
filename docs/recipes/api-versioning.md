# API Versioning

## Problem

As your modular monolith evolves, you need to change endpoint contracts -- adding fields, renaming properties, or restructuring responses. Without versioning, any breaking change disrupts existing API consumers.

## Solution

Add the `Asp.Versioning.Http` package, configure API versioning in the host, and version endpoint groups per module.

### Step 1: Install the Package

```bash
dotnet add src/EShop.Host/ package Asp.Versioning.Http
```

### Step 2: Configure API Versioning

In the host's `Program.cs`:

```csharp
using Asp.Versioning;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiVersioning(options =>
{
    // Default to v1 when no version is specified
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;

    // Report available versions in response headers
    options.ReportApiVersions = true;

    // Read version from URL segment, query string, or header
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("X-Api-Version"));
});

var app = builder.Build();
```

### Step 3: Create Versioned Endpoint Groups

Organize endpoint groups by version in your module's Api layer:

```csharp
public class CatalogModuleRegistration : IModuleRegistration
{
    public void ConfigureEndpoints(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .HasApiVersion(new ApiVersion(2, 0))
            .ReportApiVersions()
            .Build();

        // v1 endpoints
        var v1 = app.MapGroup("/api/v{version:apiVersion}/catalog")
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(new ApiVersion(1, 0));

        MapV1Endpoints(v1);

        // v2 endpoints
        var v2 = app.MapGroup("/api/v{version:apiVersion}/catalog")
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(new ApiVersion(2, 0));

        MapV2Endpoints(v2);
    }

    private static void MapV1Endpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", ProductEndpointsV1.GetById)
            .WithName("GetProductV1");

        group.MapPost("/", ProductEndpointsV1.Create)
            .WithName("CreateProductV1");
    }

    private static void MapV2Endpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", ProductEndpointsV2.GetById)
            .WithName("GetProductV2");

        group.MapPost("/", ProductEndpointsV2.Create)
            .WithName("CreateProductV2");
    }
}
```

### Step 4: Implement Versioned Endpoints

The v1 endpoint returns the original DTO:

```csharp
public static class ProductEndpointsV1
{
    public static async Task<IResult> GetById(
        Guid id,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Query(new GetProductByIdQuery(id), ct);

        return result.Match(
            onSuccess: product => Results.Ok(new
            {
                product.Id,
                product.Name,
                product.Price
            }),
            onFailure: errors => Results.NotFound(errors));
    }

    public static async Task<IResult> Create(
        CreateProduct command,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);

        return result.Match(
            onSuccess: id => Results.Created($"/api/v1/catalog/{id}", id),
            onFailure: errors => Results.BadRequest(errors));
    }
}
```

The v2 endpoint returns an enhanced DTO with additional fields:

```csharp
public static class ProductEndpointsV2
{
    public static async Task<IResult> GetById(
        Guid id,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Query(new GetProductByIdQueryV2(id), ct);

        return result.Match(
            onSuccess: product => Results.Ok(new
            {
                product.Id,
                product.Name,
                product.Price,
                product.Category,       // new in v2
                product.CreatedAt,      // new in v2
                Links = new             // HATEOAS links in v2
                {
                    Self = $"/api/v2/catalog/{product.Id}",
                    Delete = $"/api/v2/catalog/{product.Id}"
                }
            }),
            onFailure: errors => Results.NotFound(errors));
    }

    public static async Task<IResult> Create(
        CreateProductV2 command,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);

        return result.Match(
            onSuccess: id => Results.Created($"/api/v2/catalog/{id}", id),
            onFailure: errors => Results.BadRequest(errors));
    }
}
```

### Step 5: Clients Specify the Version

Clients can select the API version using any of the configured readers:

**URL segment (recommended):**
```
GET /api/v1/catalog/550e8400-e29b-41d4-a716-446655440000
GET /api/v2/catalog/550e8400-e29b-41d4-a716-446655440000
```

**Query string:**
```
GET /catalog/550e8400-e29b-41d4-a716-446655440000?api-version=2.0
```

**Header:**
```
GET /catalog/550e8400-e29b-41d4-a716-446655440000
X-Api-Version: 2.0
```

### Response Headers

When `ReportApiVersions` is enabled, all responses include headers that advertise available versions:

```
api-supported-versions: 1.0, 2.0
api-deprecated-versions:
```

## Deprecating a Version

Mark a version as deprecated when you plan to remove it:

```csharp
var versionSet = app.NewApiVersionSet()
    .HasDeprecatedApiVersion(new ApiVersion(1, 0))  // deprecated
    .HasApiVersion(new ApiVersion(2, 0))
    .ReportApiVersions()
    .Build();
```

This adds the deprecated version to the `api-deprecated-versions` response header, signaling to clients that they should migrate:

```
api-supported-versions: 2.0
api-deprecated-versions: 1.0
```

::: tip Sunset header
Consider adding a `Sunset` header with the planned removal date for deprecated versions. This gives clients a clear timeline for migration.
:::

## Discussion

API versioning is a strategy decision. Here are the common approaches and their tradeoffs:

| Strategy | Pros | Cons |
|---|---|---|
| **URL segment** (`/api/v1/`) | Explicit, easy to route, cache-friendly | URL changes between versions |
| **Query string** (`?api-version=1.0`) | URLs stay stable | Easy to forget, not as cache-friendly |
| **Header** (`X-Api-Version`) | URLs completely stable | Harder to test in browsers, less discoverable |

The `Asp.Versioning` library supports all three simultaneously. URL segment versioning is the most commonly used and the easiest for clients to understand.

### Per-Module Versioning

In a modular monolith, each module manages its own version lifecycle. The Catalog module might be on v2 while the Orders module is still on v1. This independence is one of the key advantages of the modular approach -- modules can evolve at their own pace.

### When Not to Version

Not every change requires a new version:

- **Adding optional fields to responses** -- Additive changes are backward-compatible.
- **Adding new endpoints** -- New routes do not break existing clients.
- **Bug fixes** -- Fixing a bug in an existing endpoint does not require a new version.

Create a new version only for breaking changes: removing fields, changing field types, restructuring the response shape, or changing endpoint behavior.

## See Also

- [Module Anatomy](/architecture/module-anatomy) -- The Api layer where endpoints are defined
- [CLI: add-endpoint](/cli/add-endpoint) -- Scaffolding endpoint files
