# modulus list-events

Lists integration events across all modules by scanning the scaffold convention `src/Modules/{Module}/src/{Module}.Integration/IntegrationEvents/*.cs` — the location [`modulus add-event`](./add-event) writes to.

## Usage

```bash
modulus list-events
modulus list-events --json
```

## Options

| Option | Description |
|---|---|
| `--solution`, `-s <PATH>` | Path to the solution file (default: auto-discovered). |
| `--json` | Emit `[{ "module", "name", "path" }]` instead of a table. |

Output is a `Module | Name | Path` table (paths relative to the solution root). Files created outside the convention are not detected — this is a filesystem scan, not compilation.

## See Also

- [`modulus list-consumers`](./list-consumers) · [`modulus list-entities`](./list-entities) · [`modulus list-modules`](./list-modules)
