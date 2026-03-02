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

Running `modulus add-module Catalog` inside an `EShop` solution generates:

```
src/
└── Modules/
    └── Catalog/
        ├── EShop.Modules.Catalog.Domain/
        │   ├── EShop.Modules.Catalog.Domain.csproj
        │   └── Entities/
        ├── EShop.Modules.Catalog.Application/
        │   ├── EShop.Modules.Catalog.Application.csproj
        │   ├── Commands/
        │   ├── Queries/
        │   └── Contracts/
        ├── EShop.Modules.Catalog.Infrastructure/
        │   ├── EShop.Modules.Catalog.Infrastructure.csproj
        │   ├── Persistence/
        │   │   └── CatalogDbContext.cs
        │   └── CatalogModuleInstaller.cs
        ├── EShop.Modules.Catalog.Api/
        │   ├── EShop.Modules.Catalog.Api.csproj
        │   ├── Endpoints/
        │   └── CatalogModule.cs
        └── EShop.Modules.Catalog.Integration/
            ├── EShop.Modules.Catalog.Integration.csproj
            └── Events/

tests/
└── Modules/
    └── Catalog/
        ├── EShop.Modules.Catalog.UnitTests/
        │   └── EShop.Modules.Catalog.UnitTests.csproj
        ├── EShop.Modules.Catalog.IntegrationTests/
        │   └── EShop.Modules.Catalog.IntegrationTests.csproj
        └── EShop.Modules.Catalog.ArchitectureTests/
            ├── EShop.Modules.Catalog.ArchitectureTests.csproj
            └── LayerDependencyTests.cs
```

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

1. **Updates the solution file** (`EShop.slnx`) -- All five source projects and three test projects are added to the solution with proper folder grouping.
2. **Updates `ModuleRegistration.cs`** -- The module's installer is registered in the WebApi composition root so it is discovered at startup.

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
