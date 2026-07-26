# modulus add-migration

Adds an EF Core migration for a **module's DbContext**, wrapping `dotnet ef migrations add` with the solution's project layout so you never hand-assemble `--project`/`--startup-project` paths.

## Usage

```bash
# Migration for the Orders module's OrdersDbContext
modulus add-migration AddOrderTable --module Orders

# Preview the exact dotnet-ef invocation without running it
modulus add-migration AddOrderTable --module Orders --dry-run
```

## What It Runs

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Modules/<Module>/src/<Module>.Infrastructure/<Module>.Infrastructure.csproj \
  --startup-project src/<Solution>.WebApi/<Solution>.WebApi.csproj \
  --context <Module>DbContext \
  --output-dir Migrations
```

The module's Infrastructure project owns the context, so the migration lands there (`Migrations/` by default). The WebApi host is the design-time startup project: its generated `AddAllModules(...)` registers the module's context with the real provider, so **no `IDesignTimeDbContextFactory` is needed** — and because `migrations add` never contacts the database, the placeholder `ConnectionStrings:Default` works fine.

## Arguments & Options

| Argument / Option | Description |
|---|---|
| `<migration-name>` | PascalCase migration name (e.g. `AddOrderTable`). |
| `--module`, `-m` | **Required.** The module whose DbContext the migration targets. |
| `--solution`, `-s` | Path to the `.slnx` (default: auto-find in current or parent directories). |
| `--context <NAME>` | DbContext class name (default: `{Module}DbContext`). |
| `--output-dir <DIR>` | Migrations directory relative to the Infrastructure project (default: `Migrations`). |
| `--dry-run` | Print the exact `dotnet ef` invocation and resolved paths without running anything. |

## Prerequisites

- The **dotnet-ef tool**: `dotnet tool install --global dotnet-ef`. A failed run prints this hint.
- Scaffolds created from 3.1.0 onward reference `Microsoft.EntityFrameworkCore.Design` in the WebApi host. Older scaffolds: add the `Design` package reference to the host project (with `PrivateAssets=all`) and the pin to `Directory.Packages.props`.

## Notes

- **Only the write context gets migrations.** The scaffolded `{Module}ReadOnlyDbContext` maps the identical model for no-tracking queries — never generate migrations for it.
- **Messaging tables appear in module migrations.** The scaffolded `BaseDbContext` maps the outbox/inbox tables into every module context under the module's schema; that is separate from the host-level `ModulusKit.Messaging` contexts, which are migrated by `UseModulusMessagingMigrationsAsync` (see the [messaging migrations guide](https://github.com/adamwyatt34/Modulus/blob/main/src/Modulus.Messaging/Migrations/README.md)).
- **Applying migrations** is your choice of `context.Database.MigrateAsync()` at startup or `dotnet ef database update` with the same `--project`/`--startup-project` pair.

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Migration added (or `--dry-run` preview printed). |
| `1` | Validation failure (bad name, unknown module, missing projects) or `dotnet ef` failed — the failing invocation is printed for manual re-run. |

## See Also

- [`modulus add-module`](./add-module) — where the Infrastructure project comes from
- [Outbox Pattern](/messaging/outbox-pattern) — the messaging tables mapped by `BaseDbContext`
