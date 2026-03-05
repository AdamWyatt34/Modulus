# Module Auto-Discovery

The Module Auto-Discovery generator scans all referenced assemblies for `IModuleRegistration` implementations and produces `AddAllModules()` and `MapAllModuleEndpoints()` extension methods. This eliminates the need for a manually maintained `ModuleRegistration.cs` file in the host project.

## What It Replaces

Previously, adding a module required updating the host's composition root:

```csharp
// Old approach -- manual ModuleRegistration.cs
public static class ModuleRegistration
{
    public static IServiceCollection AddModules(
        IServiceCollection services, IConfiguration configuration)
    {
        CatalogModule.ConfigureServices(services, configuration);
        OrderingModule.ConfigureServices(services, configuration);
        // Must manually add each new module here
        return services;
    }

    public static WebApplication MapModuleEndpoints(this WebApplication app)
    {
        CatalogModule.ConfigureEndpoints(app);
        OrderingModule.ConfigureEndpoints(app);
        // Must manually add each new module here
        return app;
    }
}
```

The source generator replaces this entirely:

```csharp
// New approach -- source-generated
builder.Services.AddAllModules(builder.Configuration);
app.MapAllModuleEndpoints();
```

## How It Works

1. The generator runs in the **host project** (e.g., `EShop.WebApi`)
2. It scans all **referenced assembly symbols** for types implementing `IModuleRegistration`
3. It verifies each type has both static methods: `ConfigureServices()` and `ConfigureEndpoints()`
4. It generates a `GeneratedModuleRegistration` class that calls each module in order

## What Gets Generated

```csharp
public static class GeneratedModuleRegistration
{
    public static IServiceCollection AddAllModules(
        this IServiceCollection services, IConfiguration configuration)
    {
        CatalogModule.ConfigureServices(services, configuration);
        OrderingModule.ConfigureServices(services, configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapAllModuleEndpoints(
        this IEndpointRouteBuilder app)
    {
        CatalogModule.ConfigureEndpoints(app);
        OrderingModule.ConfigureEndpoints(app);
        return app;
    }
}
```

## Module Ordering

By default, modules are registered in alphabetical order by fully qualified type name. Control the order with the `[ModuleOrder]` attribute:

```csharp
using Modulus.Mediator.Abstractions;

[ModuleOrder(1)]
public class CatalogModule : IModuleRegistration
{
    public static IServiceCollection ConfigureServices(
        IServiceCollection services, IConfiguration configuration) { /* ... */ }

    public static IEndpointRouteBuilder ConfigureEndpoints(
        IEndpointRouteBuilder app) { /* ... */ }
}

[ModuleOrder(2)]
public class OrderingModule : IModuleRegistration
{
    public static IServiceCollection ConfigureServices(
        IServiceCollection services, IConfiguration configuration) { /* ... */ }

    public static IEndpointRouteBuilder ConfigureEndpoints(
        IEndpointRouteBuilder app) { /* ... */ }
}
```

Lower values execute first. Modules with equal order values are sorted alphabetically.

## Usage in Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register all module services (source-generated)
builder.Services.AddAllModules(builder.Configuration);

var app = builder.Build();

// Map all module endpoints (source-generated)
app.MapAllModuleEndpoints();

app.Run();
```

## Impact on CLI

The `modulus add-module` command is simplified by this generator:

- **Before**: The CLI had to modify `ModuleRegistration.cs` to add the new module's registration calls
- **After**: The CLI only needs to create the module projects and add them to the solution. The generator picks up the new `IModuleRegistration` implementation automatically on the next build

## Generator Diagnostic

| ID | Severity | Message |
|---|---|---|
| MODGEN004 | Warning | `IModuleRegistration` implementation is missing `ConfigureServices()` or `ConfigureEndpoints()` |

If a type implements `IModuleRegistration` but is missing one of the required static methods, the generator emits a warning and skips that module from auto-registration.

## See Also

- [Handler Registration](./handler-registration) -- Auto-register handlers within each module
- [Architecture Overview](/architecture/) -- How modules compose into the host
- [CLI: add-module](/cli/add-module) -- Adding modules to your solution
