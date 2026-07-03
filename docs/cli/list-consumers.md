# modulus list-consumers

Lists integration event handlers across all modules by scanning the scaffold convention `src/Modules/{Module}/src/{Module}.Infrastructure/IntegrationEventHandlers/*Handler.cs` — the location [`modulus add-consumer`](./add-consumer) writes to.

## Usage

```bash
modulus list-consumers
modulus list-consumers --json
```

## Options

| Option | Description |
|---|---|
| `--solution`, `-s <PATH>` | Path to the solution file (default: auto-discovered). |
| `--json` | Emit `[{ "module", "name", "path" }]` instead of a table. |

Output is a `Module | Name | Path` table (paths relative to the solution root). Only `*Handler.cs` files in the convention folder are listed — this is a filesystem scan, not compilation.

## See Also

- [`modulus list-events`](./list-events) · [`modulus list-entities`](./list-entities) · [`modulus list-modules`](./list-modules)
