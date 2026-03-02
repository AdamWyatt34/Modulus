# modulus list-modules

Lists all modules in the current Modulus solution. This is a quick way to see what modules exist and verify your solution structure.

## Synopsis

```bash
modulus list-modules [options]
```

## Options

| Option | Description | Default |
|---|---|---|
| `--solution, -s <path>` | Path to the `.slnx` solution file. If omitted, the CLI auto-discovers the nearest solution by walking up the directory tree. | Auto-discovered |

## Example Output

```bash
$ modulus list-modules

Modules in EShop.slnx:
  - Catalog
  - Orders
  - Identity
  - Notifications

4 module(s) found.
```

If no modules have been added yet:

```bash
$ modulus list-modules

Modules in EShop.slnx:
  (none)

0 module(s) found.
```

## Examples

**List modules using auto-discovery (run from anywhere inside the solution):**

```bash
modulus list-modules
```

**List modules from a specific solution file:**

```bash
modulus list-modules --solution ~/projects/EShop/EShop.slnx
```

## See Also

- [modulus add-module](./add-module) -- Add a new module to the solution
- [modulus init](./init) -- Create a new solution
