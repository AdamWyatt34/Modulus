# modulus upgrade

Bumps every `ModulusKit.*` package pin in the solution's `Directory.Packages.props` to a target version, preserving the file's formatting and comments. Non-ModulusKit pins are never touched.

## Usage

```bash
# Upgrade to the version matching the installed CLI (recommended after `dotnet tool update`)
modulus upgrade

# Preview without writing
modulus upgrade --dry-run

# Pin an explicit version
modulus upgrade --version 2.0.0
```

## Options

| Option | Description |
|---|---|
| `--version <VERSION>` | Target `ModulusKit.*` version. Default: the CLI's own version, so `dotnet tool update --global ModulusKit.Cli` followed by `modulus upgrade` moves the solution to the matching library set. |
| `--solution`, `-s <PATH>` | Path to the solution file. Default: auto-discovered by walking up from the current directory. |
| `--dry-run` | Print the from→to table without writing `Directory.Packages.props`. |

## Behavior

1. Locates `Directory.Packages.props` next to the solution file.
2. Identifies every `<PackageVersion Include="ModulusKit.*" ...>` entry (XML-parsed, so unusual layouts are detected rather than guessed at).
3. Rewrites only the `Version="..."` substring of those entries — indentation, comments, and everything else in the file survive byte-for-byte.
4. Prints a from→to table and suggests `dotnet restore` + [`modulus doctor`](./doctor) to verify.

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Pins updated (or already at the target). |
| `1` | Solution or props file not found, malformed XML, or an entry that could not be rewritten (reported for manual follow-up). |

## Typical Workflow

```bash
dotnet tool update --global ModulusKit.Cli
cd MySolution
modulus upgrade --dry-run   # review
modulus upgrade             # apply
dotnet restore
modulus doctor              # verify version consistency and solution health
```
