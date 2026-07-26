# Release notes fragment — Wave B item 2 (multi-target library packages)

Append under the `[4.0.0-preview]` (or equivalent) heading in `CHANGELOG.md`. Not applied directly
to `CHANGELOG.md` per task instructions — copy the bullet(s) below in by hand.

### Added

- **Multi-targeting `net8.0;net10.0`** (audit item 5, `docs/audit/wave-b-4.0-plan.md` §2): the seven
  `ModulusKit.*` runtime library packages — `Mediator`, `Mediator.Abstractions`, `Messaging`,
  `Messaging.Abstractions`, `Messaging.RabbitMq`, `Messaging.AzureServiceBus`, and `Testing` — now
  ship both `net8.0` and `net10.0` assemblies in one package, so LTS-pinned consumers on .NET 8 can
  reference them without adopting .NET 10. The audit text said "net8.0/net9.0"; net9.0 is a
  Standard-Term-Support release that leaves support in May 2026, so the shipped set is
  **`net8.0;net10.0`** — both LTS, no dead-on-arrival middle target. `ModulusKit.Cli` (a
  `RollForward=LatestMajor` dotnet tool), `ModulusKit.Templates`-embedded scaffolds, and the
  Roslyn-hosted `ModulusKit.Analyzers`/`ModulusKit.Generators`/code-fixes stay single-target
  (`net10.0` / `netstandard2.0` respectively) — a tool always runs on the SDK's own version, and
  scaffolds/analyzers have no reason to multi-target.

### Changed

- **`Directory.Build.props`** now defaults every project to `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`;
  single-target projects override with their own `<TargetFramework>` (the SDK only cross-targets
  when `TargetFrameworks` is set *and* `TargetFramework` is empty, so a project-level override wins
  cleanly). `Directory.Build.targets` clears `TargetFrameworks` for any project that sets its own
  `TargetFramework`, because NuGet Restore reads `TargetFrameworks` whenever it's non-empty
  regardless of a `TargetFramework` override — without the clear, a `netstandard2.0`-targeting
  project's restore silently produces a `net8.0`/`net10.0` assets file instead (NETSDK1005 at build
  time), and a `net10.0`-targeting project restores (harmlessly, but wastefully) an unused `net8.0`
  target.
- **`Directory.Packages.props`** gains TFM-conditional `PackageVersion` pins: EF Core is the one
  hard fork in this multi-target (10.0.x ships no `net8.0` assets at all), so
  `Microsoft.EntityFrameworkCore`/`.Relational`/`.InMemory`/`.SqlServer`/`.Sqlite`/`.Design`/`.Tools`
  and `Microsoft.Data.Sqlite` are pinned to `8.0.29` (the latest EF8 patch at the time of writing)
  under `Condition="'$(TargetFramework)' == 'net8.0'"`, with the existing `10.0.3` rows conditioned
  to `net10.0`. Every other pinned package (`Microsoft.Extensions.*` 10.x, `RabbitMQ.Client`,
  `Azure.Messaging.ServiceBus`/`Azure.Core`, `System.CommandLine`, `Shouldly`, `xunit*`) already
  ships `net8.0` and/or `netstandard2.0` assets and needed no change.
- **`Modulus.Messaging.AzureServiceBus`**: `AzureServiceBusTransport`'s two client-construction
  locks switch from `System.Threading.Lock` (a `net9.0`+ type, incompatible with `net8.0`) to plain
  `object` monitors on both TFMs. These locks guard a lazy, effectively-uncontended one-time client
  construction, so `Lock`'s uncontended-fast-path win is immaterial here — one code path for both
  TFMs beats an `#if NET9_0_OR_GREATER` shim for no measurable benefit. `AzureServiceBusTopology`
  swaps `Convert.ToHexStringLower` (also `net9.0`+) for `Convert.ToHexString(...).ToLowerInvariant()`,
  identical output on both TFMs.
- Six `Modulus.Messaging.Tests` call sites that asserted directly on `TransportEnvelope.Headers`
  (`IReadOnlyDictionary<string, string>?`) via Shouldly's `ShouldContainKey`/`ShouldNotContainKey`
  now assert via `.Keys.ShouldContain`/`.Keys.ShouldNotContain` instead: Shouldly 4.3.0's `net8.0`
  build only overloads that dictionary assertion for `IDictionary<,>`, while its `net9.0` build
  (the asset a `net10.0` project restores) also overloads it for `IReadOnlyDictionary<,>` — a real
  cross-TFM API-surface difference in the third-party package, not a Modulus regression.
- Test suites covering multi-targeted library code now multi-target too, so `dotnet test` exercises
  both TFMs: `Modulus.Mediator.Tests`, `Modulus.Messaging.Tests`, `Modulus.Testing.Tests`,
  `Modulus.Messaging.RabbitMq.IntegrationTests`, `Modulus.Messaging.AzureServiceBus.IntegrationTests`.
  `Modulus.Cli.Tests`/`.IntegrationTests`, `Modulus.Generators.Tests`, `Modulus.Analyzers.Tests`, and
  `Modulus.Templates.Tests` stay `net10.0`-only (they cover `net10.0`-only projects).
- CI (`.github/workflows/ci.yml`): the `build`, `messaging-integration`, and `asb-integration` jobs'
  "Setup .NET" steps now install both the `8.0.x` and `10.0.x` SDKs (`actions/setup-dotnet`'s
  multi-version form) — `dotnet test` actually executes the `net8.0` test binaries in-process, which
  needs the matching shared runtime installed, not just build-time reference assemblies. No job
  restructuring: still one `build`/`test` step per job, not a TFM matrix.

### Packaging note

- This is bundled into the same 4.0.0 major as the other three breaking Wave B items even though
  multi-targeting is additive for consumers on its own — the EF8 pin (a real behavioral fork versus
  EF10 for anyone touching the outbox/inbox store on `net8.0`) and the coordinated all-packages-one-
  version release convention (`Directory.Packages.props` pins all ten packages together, same as
  2.0.0/3.0.0) mean it rides behind the same major rather than shipping as its own minor.
- Regenerating the four `CompatibilitySuppressions.xml` files named in the task
  (`Mediator.Abstractions`, `Mediator`, `Messaging.Abstractions`, `Messaging`) turned out to be a
  no-op: none exist in this repo state, and `dotnet pack ... /p:GenerateCompatibilitySuppressionFile=true`
  at `MinVerVersionOverride=4.0.0` against the `3.0.0` package-validation baseline created none —
  adding a target framework alongside an existing one is treated as additive, not breaking, by
  `Microsoft.DotNet.PackageValidation`. See the deviations note in the PR/task summary.
