# Missing Features & Nice-to-Haves — Backlog Plan

Backlog of work outside the top-10 fix plan ([`top-10-fixes-plan.md`](top-10-fixes-plan.md)). This document is status-aware: completed items stay listed for auditability. The July 2026 sweep ([`gap-analysis-2026-07.md`](gap-analysis-2026-07.md)) drove the PR27–PR36 wave, which shipped in full with the coordinated **2.0.0** release.

## Done

One line each; details live in the linked docs, tests, and CHANGELOG.

- **PR6 — E2E CLI integration test**: `init` → `add-module` → `dotnet build`, tagged `Category=E2E`, Linux + Windows.
- **PR7 — Per-package versioning**: MinVer tag prefixes per package; tag-triggered single-package publish (first exercised by the 2.0.0 wave).
- **PR8 — Outbox dead-letter CLI**: `modulus outbox list-failed | retry | purge`.
- **PR9 (reframed) — Provider-agnostic messaging migrations**: consumer-owned migrations + `UseModulusMessagingMigrationsAsync()`.
- **PR10 — Generator polish**: `[GeneratedCode]`, `record class` handlers, `[ModulusModule]`.
- **PR11 — Deflaked messaging tests**: condition waits + `IOutboxDispatcher` extraction.
- **PR12 — Configuration-driven messaging**: `AddModulusMessaging(IConfiguration, ...)` binder overload.
- **PR13 — Aspire transport wiring**: `init --aspire --transport rabbitmq` provisions the AppHost RabbitMQ resource.
- **PR14 — `modulus doctor`**: six checks, `--json`/`--strict`, exit codes 0/1/2.
- **PR15 — `Modulus.Templates.Tests`**: generator + template engine coverage.
- **PR16 — Sample application**: `samples/SampleApp` (CLI-dogfooded, ProjectReference-wired, own CI job + vulnerable-package scan).
- **PR17 — Event/consumer scaffolding**: `modulus add-event` / `modulus add-consumer` with cross-module auto-wiring.
- **PR18 — Module lifecycle**: `modulus remove-module` (dry-run default); rename documented as a manual procedure (`docs/cli/rename-module.md`).
- **PR19 — Docs snippet validation**: `scripts/Validate-DocsSnippets.ps1` + CI job (33 verified snippets).
- **PR20 — Coverage reporting**: coverlet + Codecov upload (still `continue-on-error` until the `CODECOV_TOKEN` secret lands).
- **PR21 — Shell completion docs**: `docs/cli/completions.md`.
- **PR22 — Distributed tracing**: mediator `TracingBehavior`, outbox `ActivitySource`, OTel recipe.
- **PR23 — Package icon + NuGet polish**. **PR24 — Governance files.** **PR25 — `TreatWarningsAsErrors` in CI.**
- **MassTransit removal**: in-house transport layer (9 packages), migration guide, docs sweep.
- **PR27 — Scaffold-compile break fixed end-to-end**: template `using` fix, CLI-version pin fix, and E2E now scaffolds against a **HEAD-packed local feed** (`MODULUS_E2E_FEED`/`MODULUS_E2E_PACKAGE_VERSION`, `scripts/New-E2EFeed.ps1`, CI pack step) — template↔library drift fails in CI before publish, and E2E is green at untagged HEADs.
- **PR28 — Messaging 2.0 release wave**: CHANGELOG cut to `[2.0.0]`, CONTRIBUTING release procedure fixed (nine prefixes + checklist), all nine packages tagged and published at 2.0.0. `IMessageBus.Send` **removed** (zero call sites, no consuming pipeline) along with the transport point-to-point path.
- **PR29 — Inbox reservation with stale takeover**: `TryReserve`/`MarkConsumerProcessed` claim each `(EventId, handler)` pair before execution — exactly-once per handler under concurrent duplicates, at-least-once preserved under crashes via `ConsumerReservationTimeout` takeover (synergy: `dlq replay` re-runs only unfinished handlers).
- **PR30 — Operational docs**: `docs/cli/outbox.md`, corrected `UnitOfWorkBehavior` example (compile-verified), `docs/messaging/graceful-shutdown.md`, `docs/messaging/dead-letter-queues.md`, manual rename procedure, package-table fixes.
- **PR31 — Messaging health checks**: `AddHealthChecks().AddModulusMessaging()` — transport probe (`ITransportHealthProbe`) + outbox backlog thresholds; scaffolded `/readyz` filters on the `ready` tag.
- **PR32 — Messaging metrics**: `Modulus.Messaging` meter (outbox outcomes, handler durations, dedup/retry/dead-letter counters), OTel recipe updated.
- **PR33 — Broker DLQ tooling**: `modulus dlq list | replay` for RabbitMQ and Azure Service Bus, behind an `IDlqBrowser` port; topology naming classes made public.
- **PR34 — Transport test cold spots**: `OutboxProcessor`/`TransportConsumerHost` unit tests; RabbitMQ integration suite extended (restart, pre-declared topology, unknown-type) and **promoted to blocking CI**; new Azure Service Bus **emulator** integration suite (non-blocking CI, `Config.json` drift-guarded against the topology helpers).
- **PR35 — `modulus upgrade`**: bumps `ModulusKit.*` pins with `--dry-run`, formatting-preserving; Aspire version hoisted to one constant — and fixed to **Aspire 13.4.6** with the real AppHost shape (`Aspire.Hosting.Defaults` never existed on nuget.org).
- **PR36 — Sample vulnerable-package scan**: `sample-build` CI job restores and scans `SampleApp.slnx` with the same High/Critical gate as the root.
- **CLI candy**: `list-events` / `list-consumers` / `list-entities` + `--json` on all list commands.

## Remaining backlog

### Near-term

- **Promote the ASB emulator CI job to blocking** once it proves stable on hosted runners (same path the RabbitMQ job took).
- **Codecov badge** — blocked on the `CODECOV_TOKEN` repository secret; the upload step is `continue-on-error` until then.

### On demand

- **`rename-module` implementation** — still deferred as high-risk cross-cutting rename; the manual procedure is documented (`docs/cli/rename-module.md`). Revisit if users report doing it often.
- **PR26 — BenchmarkDotNet baseline** — deferred pre-adoption; revisit when real consumers ask for perf numbers.
- **Inbox reservation stale-sweep tuning** — `ConsumerReservationTimeout` is a fixed option; a per-handler override or background sweeper only if field feedback demands it.
- **`--json` on add-* scaffolders** — no consumer story yet; the list commands cover tooling reads.
- **Broker fault-injection tests** (publisher-confirm failure, connection-recovery chaos) — needs a toxiproxy-style layer; documented as untested in the transport READMEs.

## What's deliberately not on this list

- **MassTransit v8 upgrade** — resolved by removal (see `docs/messaging/migrating-from-masstransit.md`).
- **Provider-specific bundled EF migrations** — Modulus stays provider-agnostic (consumer-owned migrations).
- **MediatR migration** — Modulus deliberately ships its own mediator.
- **GraphQL / gRPC scaffolding** — out of project scope.
- **Point-to-point `Send`** — removed in 2.0; would return only as a designed feature with a real consuming pipeline.
