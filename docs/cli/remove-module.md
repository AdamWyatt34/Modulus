# modulus remove-module

Removes a module: its projects leave the solution file and its directory tree is deleted.

```bash
modulus remove-module <ModuleName> [--solution <path>] [--confirm] [--force]
```

**Dry-run by default.** Without `--confirm`, the command only prints what would happen — every project that would be removed from the `.slnx`, the directories that would be deleted, and any cross-module references it found — and changes nothing.

## Cross-module reference protection

If another module holds a `ProjectReference` into the module being removed (typically to its `Integration` project, wired by [`modulus add-consumer`](./add-consumer)), the removal is blocked and the referencing projects are listed. Remove those references first, or pass `--force` to proceed anyway — the referencing modules will then fail to build until you clean them up.

## Options

| Option | Description |
|---|---|
| `--solution`, `-s` | Path to the solution file (default: auto-discover) |
| `--confirm` | Actually apply the removal |
| `--force` | Proceed even when other modules reference this one |

## Examples

```bash
# See what would be removed
modulus remove-module Billing

# Apply it
modulus remove-module Billing --confirm

# Shipping still consumes a Billing event, remove anyway
modulus remove-module Billing --confirm --force
```

> Renaming a module remains a manual operation: a robust `rename-module` would have to rewrite namespaces, project files, folders, and generated registration names, and is deliberately not offered until it can be made safe.
