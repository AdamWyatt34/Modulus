# Missing Features & Nice-to-Haves — Backlog Plan

Backlog of work outside the top-10 fix plan ([`top-10-fixes-plan.md`](top-10-fixes-plan.md)). This document is status-aware: completed items stay listed for auditability, while active items are prioritized by package experience and release readiness.

## Completed / Mostly Done

These were originally listed as PR6-PR10. They should not drive new roadmap work unless a follow-up is called out below.

### Done — End-to-end CLI integration test

- `tests/Modulus.Cli.IntegrationTests/InitAddModuleBuildTests.cs` covers `init` → `add-module` → `dotnet build`.
- The test is tagged `[Trait("Category", "E2E")]`.
- CI runs E2E separately on Linux and Windows.

### Done — Per-package versioning

- Packable projects use `MinVer` with package-specific tag prefixes (`cli-v`, `mediator-v`, `messaging-v`, etc.).
- `.github/workflows/ci.yml` publishes only the package matching the pushed tag prefix.
- `Directory.Build.props` no longer carries a single shared package version.

### Done — Outbox dead-letter tooling

- `modulus outbox list-failed`, `retry`, and `purge` exist.
- The CLI supports explicit connection strings and appsettings-based lookup.
- `IOutboxAdminStore` / `EfOutboxAdminStore` provide the admin surface.

### Mostly Done — Generator polish

- Generated output includes `[System.CodeDom.Compiler.GeneratedCode]`.
- `HandlerRegistrationGenerator` supports `record class` handlers.
- `ModuleRegistrationGenerator` supports `[ModulusModule]`.
- Follow-up: keep this area covered by generator snapshot tests in the active backlog.

### Reframed — Messaging migrations

The original backlog proposed shipping concrete EF Core migrations under `Modulus.Messaging`. The codebase now takes the better library shape: provider-agnostic contexts plus consumer-owned migrations, documented under `src/Modulus.Messaging/Migrations/README.md`, with `UseModulusMessagingMigrationsAsync()` as a startup helper.

Follow-up should focus on templates, docs, and validation rather than committing provider-specific generated migrations.

## Tier 1 — Best Next Additions for 1.x

### PR11 — Deflake messaging tests

Fixed sleeps still exist in messaging tests and are the most obvious reliability gap.

- Replace `Task.Delay(1000)` in `ConsumerAdapterTests`, `IdempotentConsumerAdapterTests`, `MassTransitMessageBusTests`, and `OutboxProcessorTests`.
- Add a shared `WaitForConditionAsync(Func<Task<bool>> predicate, TimeSpan timeout)` helper.
- Extract an `IOutboxDispatcher` from `OutboxProcessor` so tests can execute one dispatch pass directly instead of racing the `BackgroundService` lifetime.

### PR12 — Configuration-driven messaging setup

`modulus init --transport` writes a `Messaging` section, and the docs show configuration-driven setup, but the library does not expose a first-class binder.

- Add `AddModulusMessaging(this IServiceCollection, IConfiguration configuration, Action<MessagingOptions>? configure = null)`.
- Bind `Messaging:Transport`, `Messaging:ConnectionString`, `Messaging:FullyQualifiedNamespace`, `Messaging:OutboxBatchSize`, `Messaging:OutboxPollInterval`, and retry options.
- Keep assembly discovery explicit through the optional callback, e.g. `options.Assemblies.Add(typeof(Program).Assembly)`.
- Add tests for valid binding, invalid transport names, Azure credential/FQNS combinations, and callback overrides.
- Update generated `Program.cs.template` comments and messaging docs to use the binder.

### PR13 — Aspire transport/resource wiring

`modulus init --aspire --transport rabbitmq` should create a runnable Aspire experience, not just an AppHost that references the WebApi.

- For `--transport rabbitmq`, add a RabbitMQ resource in `AppHost` and reference it from the WebApi project.
- Emit appsettings placeholders that line up with `AddModulusMessaging(IConfiguration, ...)`.
- Keep `inmemory` as the zero-dependency default.
- Document Azure Service Bus as an external cloud resource rather than trying to provision it by default.
- Add E2E coverage for `init --aspire --transport rabbitmq` building successfully.

### PR14 — `modulus doctor`

Add a diagnostic command that validates scaffold health without forcing users to reverse-engineer generated files.

- Checks: solution shape, expected package references, package version skew, module discovery artifacts, missing `appsettings` messaging entries, unresolved generated project references, and whether outbox/inbox contexts are registered without migration guidance.
- Optional flags: `--json`, `--strict`, `--solution`.
- Exit code 0 for healthy, 1 for errors, 2 for warnings under `--strict`.
- Tests use fixture directories instead of invoking full builds.

### PR15 — Tests for `Modulus.Templates` generators

The template generator classes remain a high-regression area because small string changes affect scaffolded consumers.

- New project `tests/Modulus.Templates.Tests/Generators/`.
- One test class per generator (`CommandGenerator`, `QueryGenerator`, `EntityGenerator`, `EndpointGenerator`, `ModuleGenerator`, `InitGenerator`, etc.).
- Prefer focused assertions for critical content and snapshot-style tests for whole-file output where readability is high.
- Include explicit cases for `--no-endpoints`, package version substitution, Aspire inclusion/exclusion, and messaging config injection.

## Tier 2 — Product Completeness

### PR16 — Sample application

A sample app gives users a known-good reference for the full package suite.

- Add `samples/SampleApp/`.
- Use all three pillars: mediator, messaging/outbox, and generators.
- Define one entity, one command, one query, one integration event, and one integration event handler.
- Include a short sample README with setup, migration, run, and test commands.
- Add the sample to `Modulus.slnx` or a dedicated CI sample-build job.

### PR17 — Integration event / consumer scaffolding

Messaging is a first-class pillar, but the CLI scaffolds commands, queries, entities, and endpoints only.

- Add `modulus add-event <Name> --module <ModuleName>` to create an integration event in the module's Integration project.
- Add `modulus add-consumer <EventName> --module <ModuleName>` to create an `IIntegrationEventHandler<TEvent>` implementation in Application or Infrastructure based on existing conventions.
- Validate cross-module references stay Integration-only.
- Add docs under `docs/cli/` and messaging recipes.

### PR18 — Module lifecycle commands

Adding modules is supported; safely removing or renaming them is still manual and error-prone.

- Add `modulus remove-module <ModuleName>` with a dry-run by default or an explicit `--confirm`.
- Add `modulus rename-module <OldName> <NewName>` only if the implementation can be made robust across namespaces, project files, folders, and generated registration names.
- Start with `remove-module` if rename risk is too high.
- Tests should assert `.slnx` updates, folder deletion, and no path traversal.

### PR19 — Docs snippet validation

Docs have enough C# snippets that drift is likely. A compile check would catch stale APIs before users do.

- Add a docs-snippet test project or script that extracts fenced `csharp` snippets marked as compileable.
- Start with high-value docs: messaging, mediator, CLI first-solution, and extraction.
- Fix or mark intentionally illustrative snippets.
- Add CI coverage once the initial set is stable.

### PR20 — Code coverage reporting

- Add `coverlet.collector` centrally and to test projects.
- CI step: `dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage`.
- Upload to Codecov via `codecov/codecov-action@v4`.
- Add a README badge only after uploads are stable.

## Tier 3 — Release Polish / Optional Investments

### PR21 — Shell tab completion

- System.CommandLine 2.0.3 has built-in completion support.
- Add README and CLI docs for Bash, Zsh, and PowerShell completion.
- Test completion through System.CommandLine APIs or deterministic command output; avoid brittle terminal keypress simulations.

### PR22 — `Activity` / `ActivitySource` for distributed tracing

- Add `TracingBehavior<TRequest, TResponse>` in `src/Modulus.Mediator/Behaviors/`.
- Add an `ActivitySource` for outbox dispatch in `Modulus.Messaging`.
- Tag request/event type, success/failure, error code count, and retry/dead-letter outcome.
- Document OpenTelemetry registration.

### PR23 — Package icon + NuGet polish

- Add `assets/icon.png` (128x128 PNG).
- Set `<PackageIcon>icon.png</PackageIcon>` and pack the asset for each package.
- Ensure every package has a package README and consistent tags/description.

### PR24 — Governance cleanup

`CONTRIBUTING.md` and `SECURITY.md` exist, so only the remaining governance pieces belong here.

- Add `CODEOWNERS`.
- Add issue templates under `.github/ISSUE_TEMPLATE/`.
- Add a pull request template with test/documentation checklist.

### PR25 — `TreatWarningsAsErrors` in CI

- Add:
  ```xml
  <PropertyGroup>
    <TreatWarningsAsErrors Condition="'$(ContinuousIntegrationBuild)' == 'true'">true</TreatWarningsAsErrors>
  </PropertyGroup>
  ```
- Run a warning cleanup first.
- Keep local builds tolerant unless the repo is already warning-clean enough to make strict local builds productive.

### PR26 — BenchmarkDotNet baseline

- New project `bench/Modulus.Bench/`.
- Benchmarks: mediator dispatch overhead, outbox insert/dispatch throughput, generator cache hit vs full rebuild.
- Run manually or on a scheduled CI workflow; do not gate PRs on benchmark numbers.

## Sequencing Summary

```
Done: PR6 E2E, PR7 versioning, PR8 outbox CLI, PR10 generator polish
Reframed: PR9 provider-agnostic migration guidance

PR11 Deflake messaging tests
PR12 Configuration-driven messaging setup
PR13 Aspire transport/resource wiring
PR14 modulus doctor
PR15 Template generator tests
    └── 1.x package experience and release-readiness baseline

PR16 Sample application
PR17 Integration event / consumer scaffolding
PR18 Module lifecycle commands
PR19 Docs snippet validation
PR20 Coverage reporting
    └── Product completeness and contributor confidence

PR21+ Optional polish, observability, governance, benchmarks
```

## What's deliberately not on this list

- **MassTransit v8 upgrade** — out of scope. v8 moved to a paid commercial license; project decision is to stay on v7 with CVE monitoring.
- **`ModulusKit.Messaging.RabbitMq` / `ModulusKit.Messaging.AzureServiceBus` package split** — listed in [`code-review.md`](code-review.md) as [HIGH] but only matters once Modulus has enough consumers that transitive-dep weight becomes a complaint. Defer until requested.
- **Provider-specific bundled EF migrations** — Modulus should stay provider-agnostic. Improve templates/docs/tooling around consumer-owned migrations instead.
- **MediatR migration** — Modulus deliberately ships its own mediator. Not a missing feature.
- **GraphQL / gRPC scaffolding** — out of project scope.
