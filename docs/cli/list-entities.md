# modulus list-entities

Lists domain entities across all modules by scanning the scaffold convention `src/Modules/{Module}/src/{Module}.Domain/Entities/*.cs` — the location [`modulus add-entity`](./add-entity) writes to.

## Usage

```bash
modulus list-entities
modulus list-entities --json
```

## Options

| Option | Description |
|---|---|
| `--solution`, `-s <PATH>` | Path to the solution file (default: auto-discovered). |
| `--json` | Emit `[{ "module", "name", "path" }]` instead of a table. |

Output is a `Module | Name | Path` table (paths relative to the solution root). Files created outside the convention are not detected — this is a filesystem scan, not compilation.

## See Also

- [`modulus list-events`](./list-events) · [`modulus list-consumers`](./list-consumers) · [`modulus list-modules`](./list-modules)
