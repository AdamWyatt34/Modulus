# modulus add-module

Adds a new feature module to an existing Modulus solution. Each module is generated with a full five-layer architecture and three test projects, following clean architecture principles with enforced boundaries.

## Synopsis

```bash
modulus add-module <module-name> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `<module-name>` | PascalCase name for the module (e.g., `Catalog`, `OrderManagement`). |

## Options

| Option | Description | Default |
|---|---|---|
| `--solution, -s <path>` | Path to the `.slnx` solution file. If omitted, the CLI auto-discovers the nearest solution by walking up the directory tree. | Auto-discovered |
| `--no-endpoints` | Skip generating the Api layer project. Useful for modules that only communicate via integration events and have no HTTP surface. | Api layer included |

## Generated Output

Running `modulus add-module Catalog` inside an `EShop` solution generates the module under `src/Modules/Catalog/`, with source projects in `src/` and test projects in `tests/`. Project names are `{Module}.{Layer}`; namespaces are `{Solution}.{Module}.{Layer}` (e.g. `EShop.Catalog.Application`):

```
src/Modules/Catalog/
├── src/
│   ├── Catalog.Domain/
│   │   ├── Catalog.Domain.csproj
│   │   └── AssemblyReference.cs
│   ├── Catalog.Application/
│   │   ├── Catalog.Application.csproj
│   │   ├── Data/
│   │   │   └── IQueryDb.cs
│   │   └── Samples/
│   │       ├── GetSampleQuery.cs
│   │       └── GetSampleQueryHandler.cs
│   ├── Catalog.Infrastructure/
│   │   ├── Catalog.Infrastructure.csproj
│   │   ├── CatalogModule.cs            # IModuleRegistration — DI + endpoint wiring
│   │   └── Persistence/
│   │       ├── CatalogDbContext.cs
│   │       └── CatalogReadOnlyDbContext.cs
│   ├── Catalog.Api/
│   │   ├── Catalog.Api.csproj
│   │   └── Endpoints/
│   │       ├── CatalogEndpointRegistration.cs
│   │       └── GetSample.cs            # GET /api/catalog/sample
│   └── Catalog.Integration/
│       └── Catalog.Integration.csproj
└── tests/
    ├── Catalog.Tests.Unit/
    │   └── Catalog.Tests.Unit.csproj
    ├── Catalog.Tests.Integration/
    │   ├── Catalog.Tests.Integration.csproj
    │   ├── CatalogEndpointTests.cs
    │   └── CatalogIntegrationTestBase.cs
    └── Catalog.Tests.Architecture/
        ├── Catalog.Tests.Architecture.csproj
        └── LayerDependencyTests.cs
```

The module ships with a working sample slice -- `GetSampleQuery` in Application and the `GetSample` endpoint in Api -- so a fresh module responds at `GET /api/catalog/sample` as soon as the host runs.

### Layer Responsibilities

| Layer | Purpose |
|---|---|
| **Domain** | Entities, aggregate roots, value objects, domain events, repository interfaces |
| **Application** | Commands, queries, handlers, validators, DTOs, application contracts |
| **Infrastructure** | EF Core DbContext, repository implementations, external service integrations |
| **Api** | Minimal API endpoint definitions and module route registration |
| **Integration** | Integration events shared with other modules via the message bus |

### Automatic Updates

When you add a module, the CLI also:

1. **Updates the solution file** (`EShop.slnx`) -- All five source projects and three test projects are added to the solution with proper folder grouping (`/src/Modules/Catalog/` and `/tests/Modules/Catalog/`).
2. **Wires the host reference** -- A `ProjectReference` to `Catalog.Infrastructure` is added to `EShop.WebApi.csproj`, which is what makes the module visible to the module auto-discovery source generator.
3. **Runs `dotnet restore`** so the solution is immediately buildable.

At startup the module is then auto-discovered: the source generator scans the host's referenced assemblies for `IModuleRegistration` implementations and emits the `AddAllModules(...)` / `MapAllModuleEndpoints()` calls that `Program.cs` already makes. No manual composition root file needs to be updated.

## Examples

**Add a module with all five layers:**

```bash
modulus add-module Catalog
```

**Add a module without the Api layer (backend-only module):**

```bash
modulus add-module Notifications --no-endpoints
```

**Add a module to a specific solution:**

```bash
modulus add-module Billing --solution ./path/to/EShop.slnx
```

## See Also

- [modulus init](./init) -- Create the solution first
- [modulus list-modules](./list-modules) -- See all modules in the solution
- [modulus add-entity](./add-entity) -- Add entities to your new module
- [Module Anatomy](/architecture/module-anatomy) -- Deep dive into the five-layer structure
