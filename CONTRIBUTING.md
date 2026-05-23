# Contributing to Modulus

## Building

```powershell
dotnet build Modulus.slnx
dotnet test Modulus.slnx --filter "Category!=E2E"
```

To run the end-to-end CLI integration test (slow — scaffolds a solution and runs `dotnet build` against it):

```powershell
dotnet test Modulus.slnx --filter "Category=E2E"
```

The E2E job runs in CI on both Ubuntu and Windows.

## Releasing a package

Each of the seven `ModulusKit.*` packages is versioned independently by [MinVer](https://github.com/adamralph/minver) using a per-package git-tag prefix. Untagged builds are pre-release (`0.0.0-alpha.0.<height>`).

| Tag prefix         | Package                              |
| ------------------ | ------------------------------------ |
| `cli-v`            | `ModulusKit.Cli`                     |
| `mediator-v`       | `ModulusKit.Mediator`                |
| `mediator-abs-v`   | `ModulusKit.Mediator.Abstractions`   |
| `messaging-v`      | `ModulusKit.Messaging`               |
| `messaging-abs-v`  | `ModulusKit.Messaging.Abstractions`  |
| `generators-v`     | `ModulusKit.Generators`              |
| `analyzers-v`      | `ModulusKit.Analyzers`               |

To ship a new version of, for example, the CLI:

```powershell
git tag cli-v1.3.0
git push --tags
```

The `publish` job in `.github/workflows/ci.yml` runs only for tag pushes, picks up the prefix, and pushes **only the matching package** to NuGet. Other packages keep their previously released version.

### Coordinated releases

The scaffolded `Directory.Packages.props` pins all six `ModulusKit.*` libraries to a single version computed from the CLI's own assembly. For a clean cross-package release, tag every package at the same version:

```powershell
foreach ($prefix in 'cli-v','mediator-v','mediator-abs-v','messaging-v','messaging-abs-v','generators-v','analyzers-v') {
  git tag "${prefix}1.3.0"
}
git push --tags
```

If you publish a library at a different version than the CLI, users can pin a known-good library set with `modulus init Sample --modulus-kit-version 1.2.4`.

## Adding an analyzer rule

See the existing `Modulus.Analyzers` project — each rule follows the `MOD00x` numbering and ships with both a diagnostic descriptor (in `DiagnosticDescriptors.cs`) and a Roslyn analyzer class. Add tests under `tests/Modulus.Analyzers.Tests` using `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`.

## Adding a template

Templates live under `src/Modulus.Templates/templates/` and are embedded into the assembly. Add the new file under either `init/` or `module/`, then register it in `ResourceManifest.cs`. Test the scaffolding via `tests/Modulus.Cli.Tests/Handlers/InitHandlerTests.cs` or `AddModuleHandlerTests.cs`.
