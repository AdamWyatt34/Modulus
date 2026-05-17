# Missing Features & Nice-to-Haves — Backlog Plan

Backlog of work outside the top-10 fix plan ([`top-10-fixes-plan.md`](top-10-fixes-plan.md)). Organized into three tiers. Each item has a one-line implementation sketch so it can be picked up later without re-investigation.

## Tier 1 — Should be in a 1.x release (PR6-PR10)

### PR6 — End-to-end CLI integration test

Highest-leverage missing test. With 117 templates and string mutation in `AddModuleHandler`, regressions are inevitable without it.

- New project `tests/Modulus.Cli.IntegrationTests/` with one test using a real filesystem under `Path.GetTempPath()`.
- Test: `Init_Then_AddModule_Then_Build_Succeeds`. Run `InitHandler.ExecuteAsync` against real `FileSystem` + real `ProcessRunner`, then `AddModuleHandler.ExecuteAsync`, then `Process.Start("dotnet", ["build", slnPath])`. Assert exit code 0.
- Tag with `[Trait("Category", "E2E")]` so CI can run it on a separate job (`dotnet test --filter "Category=E2E"`).
- CI matrix: Linux + Windows since path handling differs.
- Verification: deliberately break a template, see the test fail; revert, see it pass.

### PR7 — Per-package versioning

- Remove `<Version>1.2.3</Version>` from `Directory.Build.props`.
- Add `<Version>x.y.z</Version>` to each of the 7 packable `.csproj` files.
- Add `MinVer` (lighter than `nbgv`) for git-tag-driven versioning per project: `dotnet add package MinVer` + `<MinVerTagPrefix>cli-v</MinVerTagPrefix>` etc.
- Update `.github/workflows/ci.yml` publish step to tag-per-package: `git tag mediator-v1.2.4 && git push --tags`.
- Document in `CONTRIBUTING.md` (also new in this PR).

### PR8 — Outbox dead-letter polish

PR4 introduces `Attempts` + `LastError`; PR8 builds the operator experience.

- Add `src/Modulus.Cli/Commands/OutboxCommand.cs` with subcommands: `modulus outbox list-failed` (reads `OutboxMessage WHERE Attempts >= MaxAttempts`), `modulus outbox retry <messageId>` (resets `Attempts = 0`), `modulus outbox purge <messageId>`.
- Operates via `MessagingOptions.ConnectionString` from `appsettings.json` in the cwd.
- Test with EF Core InMemory in `tests/Modulus.Cli.Tests/Commands/OutboxCommandTests.cs`.

### PR9 — EF Core migrations for outbox / inbox

Today the user must add tables themselves. Ship the migrations.

- Add `src/Modulus.Messaging/Migrations/Outbox/` and `src/Modulus.Messaging/Migrations/Inbox/`.
- `dotnet ef migrations add InitialCreate --context OutboxDbContext` and same for Inbox. Commit the generated `.cs` files.
- Document in `src/Modulus.Messaging/README.md`: consumers call `dbContext.Database.MigrateAsync()` in `Program.cs` startup.

### PR10 — Strengthen `StronglyTypedIdGenerator` output

- Fix the `:148` indentation (cosmetic but visible).
- Add `[System.CodeDom.Compiler.GeneratedCode]` attribute to all generators per the code review.
- Add `record class` handler discovery to `HandlerRegistrationGenerator.IsCandidate` so users who prefer records aren't silently excluded.
- Add `[ModulusModule]` attribute as an alternative discovery mechanism for `ModuleRegistrationGenerator`.

## Tier 2 — Quality of life (PR11-PR15)

### PR11 — Deflake messaging tests

- `tests/Modulus.Messaging.Tests/ConsumerAdapterTests.cs` (lines 39, 54), `IdempotentConsumerAdapterTests.cs` (lines 40, 56), `MassTransitMessageBusTests.cs` (~line 40), `OutboxProcessorTests.cs` (line 86).
- Replace `Task.Delay(1000)` with a `WaitForConditionAsync(Func<Task<bool>> predicate, TimeSpan timeout)` helper that polls a condition.
- For `OutboxProcessorTests`: extract `IOutboxDispatcher` interface from the inline `ProcessPendingMessages` logic so tests can drive it directly without the `BackgroundService` lifetime race documented in lines 14-24.

### PR12 — Tests for `Modulus.Templates` code generators

The nine generators (CommandGenerator, QueryGenerator, EntityGenerator, EndpointGenerator, InitGenerator, etc.) have zero direct tests.

- New project `tests/Modulus.Templates.Tests/Generators/`.
- One test class per generator with snapshot-style assertions on generated text.
- Use `Verify.Xunit` for snapshot testing — output stays in `*.received.txt` until reviewed/approved as `*.verified.txt`.

### PR13 — Shell tab completion

- System.CommandLine 2.0.3 has built-in completion.
- Add a docs section to README explaining `dotnet completion bash > ~/.bash_completion.d/modulus` (and equivalents for `pwsh`, `zsh`).
- Add a smoke test that spawns `modulus [tab]` and asserts the response contains `init`, `add`, `version`.

### PR14 — `samples/` directory

- One sample consumer at `samples/SampleApp/`:
  - Uses all three pillars (mediator, messaging, generators).
  - Defines one entity, one command, one query, one integration event.
  - `README.md` shows the dev loop.
- Add to `Modulus.slnx` so it builds as part of CI.

### PR15 — Code coverage reporting

- Add `coverlet.collector` to each test project's `Directory.Packages.props`.
- CI step: `dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage`.
- Upload to Codecov via `codecov/codecov-action@v4`.
- Add badge to README.

## Tier 3 — Optional polish (PR16+)

### PR16 — `Activity` / `ActivitySource` for distributed tracing

- New behavior `TracingBehavior<TRequest, TResponse>` in `src/Modulus.Mediator/Behaviors/`. Starts an `Activity` per request with tags for request type and result outcome.
- New `ActivitySource` at `src/Modulus.Messaging/Outbox/OutboxActivitySource.cs` so `OutboxProcessor` publishes spans per dispatch.
- Document OTel registration in README.

### PR17 — Package icon + nuget.org polish

- Add `assets/icon.png` (128x128 PNG).
- Set `<PackageIcon>icon.png</PackageIcon>` in `Directory.Build.props` + `<None Include="..\..\assets\icon.png" Pack="true" PackagePath="\" />`.
- Same for `<PackageReadmeFile>README.md</PackageReadmeFile>` where not already set.

### PR18 — BenchmarkDotNet baseline

- New project `bench/Modulus.Bench/`.
- Three benchmarks: mediator dispatch overhead (Send vs raw call), outbox throughput (1k msg insert/dispatch), generator perf (incremental cache hit vs full rebuild).
- Add a CI job that runs benchmarks on PR and posts results as a comment (dotnet/performance style).
- Don't gate CI on benchmark numbers — just track over time.

### PR19 — CONTRIBUTING.md + governance

- `CONTRIBUTING.md` with: how to add an analyzer rule (link to MOD001-MOD005 patterns), how to add a template (link to `ResourceManifest`), how to run the test suite, branch / PR conventions.
- `CODEOWNERS` file.
- Optional: issue templates under `.github/ISSUE_TEMPLATE/`.

### PR20 — `TreatWarningsAsErrors` in CI

- In `Directory.Build.props`:
  ```xml
  <PropertyGroup>
    <TreatWarningsAsErrors Condition="'$(ContinuousIntegrationBuild)' == 'true'">true</TreatWarningsAsErrors>
  </PropertyGroup>
  ```
- Local builds stay tolerant; CI is strict.
- Requires a one-time warning cleanup before flipping the switch — that cleanup is the bulk of the PR work.

## Sequencing Summary

```
PR1+PR2 (Templates + UoW)         ──┐
PR3 (CLI hardening)               ──┼──→ Top-10 fixes complete
PR4 (Messaging hardening)         ──┤
PR5 (Supply chain)                ──┘

PR6  (E2E CLI test)               ──┐
PR7  (Per-package versioning)     ──┤
PR8  (Outbox DLQ tooling)         ──┼──→ Tier 1 — 1.x release readiness
PR9  (EF migrations)              ──┤
PR10 (Generator polish)           ──┘

PR11 (Deflake tests)              ──┐
PR12 (Template generator tests)   ──┤
PR13 (Shell completion)           ──┼──→ Tier 2 — quality of life
PR14 (samples/)                   ──┤
PR15 (Coverage)                   ──┘

PR16+ — Tier 3 — optional polish, pick by current pain point
```

## What's deliberately not on this list

- **MassTransit v8 upgrade** — out of scope. v8 moved to a paid commercial license; project decision is to stay on v7 with CVE monitoring.
- **`ModulusKit.Messaging.RabbitMq` / `ModulusKit.Messaging.AzureServiceBus` package split** — listed in [`code-review.md`](code-review.md) as [HIGH] but only matters once Modulus has enough consumers that transitive-dep weight becomes a complaint. Defer until requested.
- **MediatR migration** — Modulus deliberately ships its own mediator. Not a missing feature.
- **GraphQL / gRPC scaffolding** — out of project scope.
