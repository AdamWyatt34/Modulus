# Contributing to Modulus

## Building

```powershell
dotnet build Modulus.slnx
dotnet test Modulus.slnx --filter "Category!=E2E"
```

To run the end-to-end CLI integration test (slow — scaffolds a solution and runs `dotnet build` against it):

```powershell
# Pack HEAD into a local feed first, otherwise the scaffold pins the CLI's unpublished
# MinVer version and restore fails at an untagged HEAD. The script sets
# MODULUS_E2E_FEED and MODULUS_E2E_PACKAGE_VERSION for the current session.
pwsh scripts/New-E2EFeed.ps1
dotnet test Modulus.slnx --filter "Category=E2E"
```

With those environment variables unset, the E2E tests scaffold against nuget.org using the CLI's own version — the post-publish smoke path. The E2E job runs in CI on both Ubuntu and Windows against a HEAD-packed feed.

## Releasing a package

Each of the nine `ModulusKit.*` packages is versioned independently by [MinVer](https://github.com/adamralph/minver) using a per-package git-tag prefix. Untagged builds are pre-release (`0.0.0-alpha.0.<height>`).

| Tag prefix             | Package                                  |
| ---------------------- | ---------------------------------------- |
| `cli-v`                | `ModulusKit.Cli`                         |
| `mediator-v`           | `ModulusKit.Mediator`                    |
| `mediator-abs-v`       | `ModulusKit.Mediator.Abstractions`       |
| `messaging-v`          | `ModulusKit.Messaging`                   |
| `messaging-abs-v`      | `ModulusKit.Messaging.Abstractions`      |
| `messaging-rabbitmq-v` | `ModulusKit.Messaging.RabbitMq`          |
| `messaging-asb-v`      | `ModulusKit.Messaging.AzureServiceBus`   |
| `generators-v`         | `ModulusKit.Generators`                  |
| `analyzers-v`          | `ModulusKit.Analyzers`                   |

To ship a new version of, for example, the CLI:

```powershell
git tag cli-v2.1.0
git push --tags
```

The `publish` job in `.github/workflows/ci.yml` runs only for tag pushes, picks up the prefix, and pushes **only the matching package** to NuGet. Other packages keep their previously released version.

### Coordinated releases

The scaffolded `Directory.Packages.props` pins **all nine** `ModulusKit.*` packages to a single version computed from the CLI's own assembly, and `modulus doctor` warns on version skew — so prefer coordinated releases where every package is tagged at the same version:

```powershell
foreach ($prefix in 'cli-v','mediator-v','mediator-abs-v','messaging-v','messaging-abs-v','messaging-rabbitmq-v','messaging-asb-v','generators-v','analyzers-v') {
  git tag "${prefix}2.0.0"
}
git push --tags
```

If you publish a library at a different version than the CLI, users can pin a known-good library set with `modulus init Sample --modulus-kit-version 1.2.4`, and move existing solutions with `modulus upgrade --version <x>`.

### Release checklist

1. `CHANGELOG.md`: move `[Unreleased]` into a versioned, dated section.
2. Push `main` and wait for a fully green CI run (the E2E job proves scaffolds build against HEAD-packed packages).
3. Tag all nine prefixes at the release version from that green SHA and push the tags (each tag triggers a `publish` run for its package; `--skip-duplicate` makes partial re-runs safe).
4. Verify all nine publish runs succeeded and the packages are indexed on nuget.org (indexing can lag ~15 minutes).
5. Smoke on a clean machine/directory (no `MODULUS_E2E_*` env vars): `dotnet tool install -g ModulusKit.Cli --version <x>`, `modulus init Smoke`, `dotnet build`, `modulus doctor`.

## Adding an analyzer rule

See the existing `Modulus.Analyzers` project — each rule follows the `MOD00x` numbering and ships with both a diagnostic descriptor (in `DiagnosticDescriptors.cs`) and a Roslyn analyzer class. Add tests under `tests/Modulus.Analyzers.Tests` using `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`.

## Adding a template

Templates live under `src/Modulus.Templates/templates/` and are embedded into the assembly. Add the new file under either `init/` or `module/`, then register it in `ResourceManifest.cs`. Test the scaffolding via `tests/Modulus.Cli.Tests/Handlers/InitHandlerTests.cs` or `AddModuleHandlerTests.cs`.
