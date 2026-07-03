# Missing Features & Nice-to-Haves — Backlog Plan

Backlog of work outside the top-10 fix plan ([`top-10-fixes-plan.md`](top-10-fixes-plan.md)). This document is status-aware: completed items stay listed for auditability, while active items are prioritized by package experience and release readiness. The active backlog below is driven by the July 2026 sweep in [`gap-analysis-2026-07.md`](gap-analysis-2026-07.md).

## Done

One line each; details live in the linked docs, tests, and CHANGELOG.

- **PR6 — E2E CLI integration test**: `init` → `add-module` → `dotnet build`, tagged `Category=E2E`, run on Linux + Windows (`tests/Modulus.Cli.IntegrationTests/InitAddModuleBuildTests.cs`). *Currently red at HEAD — see PR27.*
- **PR7 — Per-package versioning**: MinVer tag prefixes per package; `ci.yml` publishes only the package matching the pushed tag (all 9 prefixes verified consistent).
- **PR8 — Outbox dead-letter CLI**: `modulus outbox list-failed | retry | purge` over `IOutboxAdminStore`/`EfOutboxAdminStore`.
- **PR9 (reframed) — Provider-agnostic messaging migrations**: consumer-owned migrations + `UseModulusMessagingMigrationsAsync()` startup helper instead of bundled provider migrations.
- **PR10 — Generator polish**: `[GeneratedCode]` attribution, `record class` handlers, `[ModulusModule]` support.
- **PR11 — Deflaked messaging tests**: fixed sleeps replaced with condition waits; `IOutboxDispatcher` extracted from `OutboxProcessor` for single-pass dispatch in tests and tooling.
- **PR12 — Configuration-driven messaging**: `AddModulusMessaging(IConfiguration, Action<MessagingOptions>)` binder overload with shared validation; `RetryPolicy` and `ConsumerRetry` separated.
- **PR13 — Aspire transport wiring**: `init --aspire --transport rabbitmq` provisions the RabbitMQ AppHost resource and references it from the WebApi; E2E covers the build.
- **PR14 — `modulus doctor`**: six checks (solution shape, version skew, module layers, messaging config, project references, migration guidance), `--json`/`--strict`, exit codes 0/1/2.
- **PR15 — `Modulus.Templates.Tests`**: 41 tests over all template generators and the template engine.
- **PR16 — Sample application**: `samples/SampleApp` (CLI-dogfooded, ProjectReference-wired, `sample-build` CI job) using mediator + messaging/outbox + generators.
- **PR17 — Event/consumer scaffolding**: `modulus add-event` / `modulus add-consumer` with cross-module Integration-only `ProjectReference` auto-wiring.
- **PR18 (partial) — Module lifecycle**: `modulus remove-module` shipped with dry-run default and `--confirm`/`--force`; `rename-module` deliberately deferred (see backlog).
- **PR19 — Docs snippet validation**: `scripts/Validate-DocsSnippets.ps1` + `docs-snippets` CI job, 31 `<!-- verify -->`-marked snippets compiled against source.
- **PR20 — Coverage reporting**: coverlet collection + Codecov upload in CI (`continue-on-error` until the `CODECOV_TOKEN` secret lands; badge deferred — see backlog).
- **PR21 — Shell completion docs**: `docs/cli/completions.md` (PowerShell, Bash, Zsh via dotnet-suggest).
- **PR22 — Distributed tracing**: `TracingBehavior` in the mediator, outbox `ActivitySource`, OpenTelemetry recipe (`docs/recipes/opentelemetry.md`).
- **PR23 — Package icon + NuGet polish**: central `PackageIcon` in `Directory.Build.props`, per-package READMEs, post-MassTransit descriptions.
- **PR24 — Governance**: CODEOWNERS, issue forms, PR template.
- **PR25 — `TreatWarningsAsErrors` in CI**: warning-clean build, strict in CI / tolerant locally (`Directory.Build.props`).
- **MassTransit removal (the big one)**: in-house transport layer — `ModulusKit.Messaging` core + in-memory, `ModulusKit.Messaging.RabbitMq`, `ModulusKit.Messaging.AzureServiceBus` (9 packages total); RabbitMQ Testcontainers integration suite (CI non-blocking); `docs/messaging/migrating-from-masstransit.md`; docs sweep; Newtonsoft.Json transitive pin gone at HEAD.

## Tier 0 — Release blockers (the consumer journey is broken at the front door)

### PR27 — Fix the scaffold-compile break (one structural cause remaining)

`modulus init` output does not build against any published package set (empirically verified; details in gap-analysis §7). The blocking E2E job fails with it. Two of the three fixes shipped immediately after the sweep:

1. ~~**Template using**~~ — **done**: `templates/init/host/Program.cs.template` now imports `{{RootNamespace}}.WebApi` (where the source-generated extensions land), guarded by a Templates.Tests assertion.
2. ~~**Version pin bug**~~ — **done**: `InitHandler` now defaults `--modulus-kit-version` from the CLI's MinVer-stamped assembly instead of the unversioned Templates assembly (which always yielded the never-published `1.0.0`).
3. **E2E against HEAD packages** (remaining, structural): E2E scaffolds against nuget.org, so template↔library drift (e.g. templates referencing the unpublished `UnitOfWorkBehavior`/`IUnitOfWork`) hides until after publish — and with fix 2, a HEAD-built CLI now pins its own unpublished version, so E2E fails at restore until this lands. Pack HEAD into a temp feed in the E2E job and scaffold with `--modulus-kit-version` + `RestoreSources`. Until then, **expect the E2E job to be red at HEAD**; PR28 (publishing the 2.0 wave) also unblocks it for tagged builds.

### PR28 — Ship the messaging 2.0 release wave

- Cut CHANGELOG `[Unreleased]` into versioned entries; tag the coordinated set (`messaging-v2.0.0`, `messaging-abs-v2.0.0`, first tags for the two transport packages) plus the mediator release PR27 depends on.
- Document the multi-tag release procedure in CONTRIBUTING (which still says "six/seven packages" and omits the transport prefixes from its example loop).
- Decide `IMessageBus.Send` **before** tagging: zero call sites, no consuming pipeline — `[Obsolete]`/remove inside the open breaking window, or commit to a documented point-to-point story. Cheapest correct move: remove now, reintroduce designed.
- Side effect: fresh scaffolds stop pulling the vulnerable Newtonsoft.Json 12.0.1 through published 1.x messaging (NU1903).

## Tier 1 — Correctness and operability

### PR29 — Inbox row-level dedup (reserve-before-execute)

`ConsumerDispatcher` checks `HasBeenProcessed` before running the handler and records the consumer only after success — two concurrent duplicate deliveries can both execute. Insert the consumer record first (constraint violation ⇒ already claimed), then execute; add a concurrent-duplicate test. Until it lands, document the guarantee as at-least-once-per-handler under concurrent redelivery.

### PR30 — Operational docs debt

- `docs/cli/outbox.md` + sidebar entry — the only undocumented CLI command, and it's the ops one.
- Fix the `UnitOfWorkBehavior` example in `docs/mediator/pipeline-behaviors.md` (calls `BeginTransactionAsync`/`Commit`/`Rollback` that don't exist on the library `IUnitOfWork`) and add `<!-- verify -->`.
- Graceful-shutdown page: hosted-service stop ordering, in-flight semantics per transport, k8s guidance; pair with letting `OutboxProcessor` finish its in-flight dispatch pass on stop.
- Broker DLQ conventions page: RabbitMQ `{queue}.dlx`/`{queue}.dead-letter`, ASB native DLQ + `RetriesExhausted` reason, and how to inspect/replay with native tooling (until PR33).
- Add `ModulusKit.Generators`/`ModulusKit.Analyzers` rows to the getting-started package table (lists 7 of 9); sweep stale package counts (CLAUDE.md, CONTRIBUTING).

### PR31 — Messaging health checks

`AddModulusMessagingHealthChecks()`: broker connectivity probe + outbox backlog-depth check with a configurable threshold; wire into the scaffold's existing `AddHealthChecks()`/`/readyz`.

### PR32 — Messaging metrics (counterpart to tracing)

`Meter("Modulus.Messaging")`: outbox dispatch counter (outcome-tagged), pending-outbox gauge, consumer handler duration histogram, inbox dedup counter. Instrument `OutboxDispatcher` and `ConsumerDispatcher` (currently logging-only); extend the OpenTelemetry recipe.

### PR33 — Broker DLQ tooling

`modulus dlq list|replay --transport rabbitmq|azureservicebus`, mirroring the outbox CLI's connection-string/appsettings ergonomics. Docs-first via PR30; build after health/metrics since native broker tools cover the interim.

## Tier 2 — Hardening and upgrade path

### PR34 — Transport-layer test cold spots

- `OutboxProcessorTests` (loop lifetime, error recovery, cancellation) and `TransportConsumerHostTests` (start/stop, publish-only early return).
- Unit tests for `RabbitMqTransport` internals (connection/channel locking, confirms, DLX provisioning, nack paths) — today only non-blocking Testcontainers happy paths cover them; promote the integration job to blocking once stable.
- Azure Service Bus: transport-class unit tests; evaluate an emulator-based integration suite (currently no ASB integration tests at all).

### PR35 — `modulus upgrade`

Bump `ModulusKit.*` pins in an existing solution's `Directory.Packages.props` and run `doctor` after. Becomes important the moment 2.0 ships and 1.x scaffolds exist in the wild. Also: stop hardcoding the Aspire version (`TemplateEngine.cs` pins 9.2.0 inline).

### PR36 — CI vulnerable-package scan for the sample

The root scan never restores `samples/SampleApp/SampleApp.slnx`, which keeps its own `Directory.Packages.props` precisely to pin advisories (SQLitePCLRaw, Microsoft.OpenApi). Add an explicit scan step.

## Tier 3 — Deferred / on-demand

- **`rename-module`** — deferred as high-risk (namespaces, csproj names, folders, generated registration names). Document the manual procedure in the interim (PR30 can host it).
- **PR26 — BenchmarkDotNet baseline** — deferred pre-adoption; revisit when performance questions arrive from real consumers.
- **Codecov badge** — once uploads are stable (token secret pending; upload step is `continue-on-error`).
- **CLI candy on demand**: `list-*` for events/consumers/entities, `--json` on scaffold commands, `--dry-run` naming alignment for `remove-module`, `--version` alias.

## What's deliberately not on this list

- **MassTransit v8 upgrade** — resolved by removal: MassTransit was replaced entirely by the in-house transport layer (see `docs/messaging/migrating-from-masstransit.md`). The v7-pin-with-CVE-monitoring trade no longer exists.
- **`ModulusKit.Messaging.RabbitMq` / `ModulusKit.Messaging.AzureServiceBus` package split** — shipped as part of the transport rewrite; kept here only so the old [HIGH] entry in [`code-review.md`](code-review.md) reads as resolved.
- **Provider-specific bundled EF migrations** — Modulus stays provider-agnostic; templates/docs/tooling around consumer-owned migrations instead.
- **MediatR migration** — Modulus deliberately ships its own mediator. Not a missing feature.
- **GraphQL / gRPC scaffolding** — out of project scope.

## Sequencing Summary

```
Done: PR6-PR25 (see Done section), MassTransit removal → in-house transports (9 packages)

PR27 Fix scaffold-compile break (template using, version pin, E2E vs HEAD packages)
PR28 Messaging 2.0 release wave (tags, CONTRIBUTING, IMessageBus.Send decision)
    └── unblocks install → scaffold → build for real users; E2E goes green

PR29 Inbox reserve-before-execute
PR30 Operational docs debt (outbox CLI page, IUnitOfWork snippet, shutdown, DLQ, package table)
PR31 Messaging health checks
PR32 Messaging metrics
PR33 Broker DLQ tooling
    └── run → observe → operate parity with the outbox/tracing work

PR34 Transport test cold spots
PR35 modulus upgrade (+ Aspire version pin)
PR36 Sample vulnerable scan
    └── hardening + the upgrade leg of the journey

Deferred: rename-module, PR26 benchmarks, Codecov badge, CLI candy
```
