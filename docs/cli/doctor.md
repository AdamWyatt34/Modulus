# modulus doctor

Validates the health of a Modulus-scaffolded solution without building it.

```bash
modulus doctor [--solution <path>] [--json] [--strict]
```

## What it checks

| Check | Severity | What it looks for |
|---|---|---|
| Solution shape | Fail | A `.slnx` exists (auto-discovered or via `--solution`) with the expected `src/` layout |
| Package versions | Warn | All `ModulusKit.*` entries in `Directory.Packages.props` share one version |
| Module artifacts | Warn | Each module under `src/Modules/<Name>/` has its Domain/Application/Infrastructure/Integration projects |
| Messaging config | Warn | If `ModulusKit.Messaging` is referenced: the `Messaging` section exists, `Transport` is valid, and broker transports have connection settings |
| Project references | Fail | Every `<ProjectReference>` target exists on disk |
| Migration guidance | Warn | `AddModulusOutbox`/`AddModulusInbox` calls are paired with `UseModulusMessagingMigrationsAsync()` |

## Options

| Option | Description |
|---|---|
| `--solution`, `-s` | Path to the solution file (default: auto-discover) |
| `--json` | Emit a single JSON document instead of human-readable output |
| `--strict` | Warnings affect the exit code |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | All checks pass (warnings allowed without `--strict`) |
| `1` | At least one check failed |
| `2` | Warnings present and `--strict` was passed |

## Examples

```bash
# Human-readable report
modulus doctor

# Machine-readable, warnings fail the build — useful as a CI gate
modulus doctor --json --strict
```
