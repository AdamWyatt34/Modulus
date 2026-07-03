# Gap Analysis — July 2026

Fresh-eyes sweep of the repo after the MassTransit removal / in-house transport release wave, evaluated against the full consumer journey: **install → scaffold → build → run → observe → operate → upgrade**. Findings are severity-tagged with file evidence and a one-line action. Sections end with what was checked and found fine, so absence of a finding is a statement, not an omission.

> Headline: the library code, tests, and CI plumbing are in good shape. The single worst problem is at the very front of the consumer journey — **a fresh `modulus init` scaffold does not compile against any published package set** (three stacked causes, all verified empirically on 2026-07-03; see §7a–c). Everything else is normal-shaped backlog.

---

## 1. API surface consistency

**Verdict: strong.** The post-MassTransit surface is deliberate and consistent.

| Finding | Severity | Evidence | Action |
|---|---|---|---|
| `IMessageBus.Send` (2 overloads) has **zero production call sites** — only one negative-path test — and no consuming pipeline exists for point-to-point commands (only published events are dispatched to handlers) | Medium | `src/Modulus.Messaging.Abstractions/IMessageBus.cs`; sole call site `tests/Modulus.Messaging.Tests/MessageBusTests.cs:107` | Decide: `[Obsolete]` + remove in 2.0 (breaking window is open now), or document/dogfood a point-to-point sample. Don't ship 2.0 without deciding — see §7a of the backlog |
| Stale `obj/Release` AssemblyInfo still carries the old "MassTransit integration" description | Low | `src/Modulus.Messaging/obj/Release/net10.0/Modulus.Messaging.AssemblyInfo.cs` | Cosmetic; clean `bin`/`obj` — no repo change needed |

**Checked and fine:**
- Extension naming is uniformly `AddModulus*` across all packages (`AddModulusMediator`, `AddModulusMessaging`, `AddModulusOutbox`/`Inbox`, `AddModulusRabbitMqTransport`, `AddModulusAzureServiceBusTransport`, `UseModulusMessagingMigrationsAsync`).
- Options shape is deliberately unified: `EndpointName`, `PrefetchCount`, `AutoProvision`, `ConnectionString`, `Credential`, `FullyQualifiedNamespace` all on `MessagingOptions` with transport-aware validation in DI (`src/Modulus.Messaging/DependencyInjection/ServiceCollectionExtensions.cs`). No duplicate transport option classes; no awkwardness found.
- Result-pattern adherence is clean: expected failures return `Result`/`Error`; exceptions are reserved for programmer error / misconfiguration (`ArgumentNullException`, `InvalidOperationException` on missing transport registration).
- Visibility discipline is good: all transports, stores, dispatchers, the processor, serializer, and registry are `internal`; the public types (`TransportEnvelope`, `TransportSubscription`, `MessageDispatchResult`, behaviors, DbContexts, message entities) are public for defensible reasons. **No accidental public leaks found.**
- `Azure.Core` in the core `ModulusKit.Messaging` package is an accepted, csproj-comment-documented trade-off so `MessagingOptions.Credential` (`TokenCredential`) stays on the core options type usable with config-driven transport selection (`src/Modulus.Messaging/Modulus.Messaging.csproj`, `MessagingOptions.cs:31`). Recommend keeping; revisit only if a consumer complains about the transitive dep.
- No MassTransit naming/leftovers in live code — the one source mention is a helpful historical comment in `src/Modulus.Messaging/Dispatch/RetryDelayCalculator.cs:7`.

## 2. Docs coverage vs shipped features

| Finding | Severity | Evidence | Action |
|---|---|---|---|
| `modulus outbox` (list-failed / retry / purge) has **no docs page and no sidebar entry** — the only CLI command missing one, and it's the operational one | High | no `docs/cli/outbox.md`; sidebar `docs/.vitepress/config.mts:132-145` lists every other command | Write `docs/cli/outbox.md` covering all three subcommands + connection-string/appsettings lookup; add sidebar entry |
| `UnitOfWorkBehavior` doc example calls `BeginTransactionAsync`/`CommitAsync`/`RollbackAsync` — none exist on the library's `IUnitOfWork` (single member: `Task<int> SaveChangesAsync(CancellationToken)`); the snippet is not `<!-- verify -->`-marked so validation never catches it | High | `docs/mediator/pipeline-behaviors.md:218-256` vs `src/Modulus.Mediator.Abstractions/Persistence/IUnitOfWork.cs`; marker convention `scripts/Validate-DocsSnippets.ps1:35` | Rewrite the example around `SaveChangesAsync` (or clearly label the transactional variant as a user-owned richer interface) and add the verify marker |
| No graceful-shutdown/drain documentation anywhere in `docs/` (no hits for shutdown/drain/StopAsync) despite two hosted services with non-obvious stop semantics | Medium | `docs/` grep; behavior lives in `src/Modulus.Messaging/Outbox/OutboxProcessor.cs`, `src/Modulus.Messaging/TransportConsumerHost.cs` | Add an operations page: stop ordering (consumer host registered first, outbox second — correct), in-flight behavior per transport, k8s guidance |
| Getting-started package table lists 7 packages — missing `ModulusKit.Generators` and `ModulusKit.Analyzers` (root README's table at `README.md:427-435` is complete with all 9) | Medium | `docs/getting-started/index.md:76-82` | Add the two rows; sweep other package-count claims (`CLAUDE.md` also says "7 NuGet packages") |

**Checked and fine:**
- `EndpointName`, `PrefetchCount`, `AutoProvision` are all documented in `docs/messaging/transports.md:99-107` and match `src/Modulus.Messaging/MessagingOptions.cs`.
- Both transport packages, config binding, retry policies (`RetryPolicy` vs `ConsumerRetry`), and inbox idempotency are documented; `docs/messaging/migrating-from-masstransit.md` is comprehensive.
- No stale current-tense MassTransit claims in docs or README.
- `docs/cli/doctor.md`, `remove-module.md`, `add-event.md`, `add-consumer.md`, `version.md`, `completions.md` all exist and are in the sidebar; `docs/recipes/opentelemetry.md` covers TracingBehavior/MetricsBehavior/ActivitySources; `docs/recipes/health-checks.md` exists.

## 3. CLI lifecycle completeness

Artifact × verb matrix (verified against `src/Modulus.Cli/Program.cs` + `Commands/*.cs`):

| Artifact | add | list | remove | rename |
|---|---|---|---|---|
| solution (`init`) | ✓ | — | — | — |
| module | ✓ | ✓ | ✓ (dry-run default, `--confirm` to apply, `--force` to skip dependency checks) | ✗ (deferred, known) |
| entity / command / query / endpoint / event / consumer | ✓ | ✗ | ✗ | ✗ |

| Finding | Severity | Evidence | Action |
|---|---|---|---|
| No post-scaffold **upgrade path**: `--modulus-kit-version` exists only on `init`; nothing bumps `ModulusKit.*` pins in an existing solution's `Directory.Packages.props`. With the 2.0 breaking release imminent this is the weakest "upgrade" leg of the journey | Medium (High once 2.0 ships) | version flow: `src/Modulus.Templates/InitOptions.cs`, `templates/init/solution/Directory.Packages.props.template:9-20` | Add `modulus upgrade` (rewrite ModulusKit pins, run `doctor` after); pairs naturally with the doctor version-skew check |
| Aspire package version hardcoded to `9.2.0` in code, not centrally managed | Medium | `src/Modulus.Templates/TemplateEngine.cs:202-204` | Move to a constant next to `ModulusKitVersion` handling or a template token; add a Templates.Tests assertion |
| `rename-module` absent (namespaces, csproj names, folders, generated registration names) | Medium | `src/Modulus.Cli/Commands/` (no command) | Keep deferred as high-risk; document the manual procedure in `docs/cli/` meanwhile |
| No `list-*` for events/consumers/entities; no `--json` on scaffold commands (only `doctor --json`); `remove-module --confirm` naming diverges from the more conventional `--dry-run` | Low | `src/Modulus.Cli/Commands/RemoveModuleCommand.cs:22-24`, `DoctorCommand.cs:17-20` | Backlog candy; take only on demand |

**Checked and fine:**
- Option shapes are consistent: `--module` (required, `-m`) and `--solution` (optional, `-s`, auto-find) everywhere; single shared `PropertyParser` for `add-entity`/`add-event`; exit codes uniform (0/1, doctor adds 2 under `--strict`).
- `modulus doctor` performs six meaningful checks (solution shape, ModulusKit version skew, module layer completeness, messaging config/transport validity, dangling ProjectReferences, outbox/inbox migration-call guidance) — `src/Modulus.Cli/Handlers/DoctorHandler.cs`.
- Templates contain zero MassTransit references; scaffold ships `/healthz` + `/readyz` endpoints.

## 4. Test cold spots

Density: Cli.Tests ~226 facts / Messaging.Tests ~117 / Mediator.Tests ~75 / Analyzers 56 / Templates 41 / Generators 33. Mediator, CLI handlers, analyzers, generators, and template generators are all well covered. The cold spots cluster in the **new transport layer's hosting and broker-facing code**:

| Cold spot | Severity | Evidence | Action |
|---|---|---|---|
| `OutboxProcessor` polling loop (BackgroundService lifetime, error-recovery delay, cancellation) has no direct tests — only the extracted `OutboxDispatcher` is tested | High | `src/Modulus.Messaging/Outbox/OutboxProcessor.cs` vs `tests/Modulus.Messaging.Tests` (no `OutboxProcessorTests`) | Add loop/lifetime tests (dispatch pass invoked, exception recovery, clean stop) |
| `TransportConsumerHost` (IHostedService start/stop, publish-only early-return) untested except via non-blocking Testcontainers job | High | `src/Modulus.Messaging/TransportConsumerHost.cs` | Add unit tests with a fake `IMessageTransport` |
| `RabbitMqTransport` internals (connection/channel locking, publisher confirms, DLX provisioning, nack paths) have no unit tests — only topology/mapper/factory units plus happy-path integration tests that CI treats as non-blocking | High | `src/Modulus.Messaging.RabbitMq/RabbitMqTransport.cs` (~248 lines); `.github/workflows/ci.yml:73-78` (`continue-on-error: true`) | Unit-test the failure/concurrency paths; consider promoting the integration job to blocking once stable |
| `AzureServiceBusTransport` has **no integration test project at all** and the transport class itself has no direct tests (only topology/mapper/factory) | Medium | `src/Modulus.Messaging.AzureServiceBus/` vs `tests/` (no ASB integration project) | At minimum unit-test the transport with SDK fakes; evaluate an emulator-based integration suite |
| Inbox concurrency: no test covers two concurrent duplicate deliveries (see §6 race) | Medium | `tests/Modulus.Messaging.Tests/Inbox/EfInboxStoreTests.cs`, `ConsumerDispatcherTests.cs` | Add a concurrent-duplicate test alongside the §6 fix |

## 5. Packaging / versioning

| Finding | Severity | Evidence | Action |
|---|---|---|---|
| Scaffold pins `ModulusKit.*` to a version that **never existed** (1.0.0) — `InitOptions.ResolveDefaultVersion()` reads the *Modulus.Templates* assembly's informational version, but Templates has no MinVer reference so it is always `1.0.0`, regardless of the CLI's real version. NuGet silently rolls forward to ancient 1.0.1 (NU1603 warnings on every fresh scaffold) | High | `src/Modulus.Templates/InitOptions.cs:33-50` (doc comment claims "the CLI's own assembly version"), `src/Modulus.Templates/Modulus.Templates.csproj` (no MinVer); reproduced: fresh scaffold restores 1.0.1 with 10+ NU1603 warnings | Resolve the version from the **CLI entry assembly** (or flow it from `Modulus.Cli` which does have `MinVerTagPrefix cli-v`), and make E2E assert the pinned version is the intended one |
| No documented release procedure for the messaging **2.0 coordinated bump** (four tags: `messaging-v2.0.0`, `messaging-abs-v2.0.0`, `messaging-rabbitmq-v1.0.0`/`2.0.0`, `messaging-asb-v...`); CONTRIBUTING still says "six/seven packages" and its release-loop example omits the two transport packages | Medium | `CONTRIBUTING.md:20-30, 43-50`; CHANGELOG `[Unreleased]` documents the break well | Add a "Releasing messaging 2.0" section; fix package counts and the example loop |
| Until 2.0 is published, fresh scaffolds transitively restore **vulnerable Newtonsoft.Json 12.0.1** (NU1903 high) through published 1.x messaging — the fix already exists at HEAD but is unreleased | Medium | reproduced in scaffold build output; CHANGELOG notes the pin removal at HEAD | Shipping the 2.0 wave resolves it; note in the release checklist |
| Root CI vulnerable-package check restores/scans only the root solution — never `samples/SampleApp/SampleApp.slnx`, which maintains its own `Directory.Packages.props` precisely to pin `SQLitePCLRaw.lib.e_sqlite3 3.50.3` and `Microsoft.OpenApi 2.9.0` against known advisories | Medium | `.github/workflows/ci.yml:37-48`; `samples/SampleApp/Directory.Packages.props` | Add a scan step for the sample solution (defense-in-depth; the sample is ProjectReference-wired so exposure is docs-grade, not release-grade) |

**Checked and fine:**
- All 9 packable projects have `MinVerTagPrefix` values that exactly match both `ci.yml` `on.push.tags` patterns and the publish `prefixMap` (`ci.yml:6-15, 187-197`). No mismatches, no missing packages.
- Package icon is set centrally (`Directory.Build.props:20,24` — `assets/icon.png` packed into every package). Per-package READMEs exist for all 8 library packages; the CLI packs the root README (unconventional but functional). Descriptions are post-MassTransit accurate.
- Root `Directory.Packages.props` is clean — no MassTransit leftovers; transitive pins documented with advisory IDs. `global.json` (10.0.103, rollForward latestFeature) is consistent with CI's `10.0.x`.
- Root `README.md:427-435` lists all 9 packages correctly.

## 6. Operational gaps

| Finding | Severity | Evidence | Action |
|---|---|---|---|
| **Inbox dedup is check-then-act**: `HasBeenProcessed` is read before handler execution and `RecordConsumer` is written only after it succeeds, so two *concurrent* deliveries of the same event can both execute the handler (the `InboxMessage` insert race is caught, but the consumer record has no insert-first reservation; PK on `(InboxMessageId, Name)` exists but is checked too late to prevent double execution) | High | `src/Modulus.Messaging/Dispatch/ConsumerDispatcher.cs:126-139`; `src/Modulus.Messaging/Inbox/EfInboxStore.cs` (`Save` catches `DbUpdateException`; `RecordConsumer` does not); `InboxDbContext.cs:29-33` | Reserve-before-execute: insert the consumer record first, catch the constraint violation as "already claimed", then run the handler (with a completion flag), or document the guarantee honestly as at-least-once-per-handler under concurrency and roadmap row-level dedup |
| **No broker DLQ tooling or docs.** Both transports correctly dead-letter after retry exhaustion (RabbitMQ: `{queue}.dlx`/`{queue}.dead-letter` provisioned, `BasicNackAsync(requeue: false)`; ASB: native `DeadLetterMessageAsync("RetriesExhausted")`), but there is no inspect/replay story — `modulus outbox` covers only the *outbox table* | High | `src/Modulus.Messaging.RabbitMq/RabbitMqTopology.cs:20-26`, `RabbitMqTransport.cs:145-158,208`; `src/Modulus.Messaging.AzureServiceBus/AzureServiceBusTransport.cs:126-131` | Near term: docs page naming the DLQ conventions and native inspection tools per broker. Later: `modulus dlq list/replay --transport ...` |
| **No messaging health checks** — no `IHealthCheck` for broker connectivity or outbox backlog depth anywhere in `src/`; scaffold registers bare `AddHealthChecks()` | Medium | grep of `src/`; `src/Modulus.Templates/templates/init/host/Program.cs.template:15` | Ship `AddModulusMessagingHealthChecks()` (broker probe + pending-outbox threshold gauge); wire into scaffold |
| **No metrics counterpart to tracing** in messaging: outbox has an ActivitySource (`src/Modulus.Messaging/Outbox/OutboxDispatcher.cs`) and the mediator has `MetricsBehavior`, but there are no counters/gauges for dispatch outcomes, consumer duration, backlog depth, or dedup hits; `ConsumerDispatcher` has neither tracing nor metrics | Medium | `src/Modulus.Messaging/Dispatch/ConsumerDispatcher.cs` (logging only) | Add a `Meter("Modulus.Messaging")` with dispatch counter, backlog gauge, consumer histogram; extend the OTel recipe |
| Outbox processor stop semantics are abrupt (breaks on first cancellation signal; no drain of the in-flight dispatch pass) and undocumented; hosted-service registration order is correct (consumers stop first) | Medium | `src/Modulus.Messaging/Outbox/OutboxProcessor.cs:6-31`; `TransportConsumerHost.cs:8-13,34-37` | Let the in-flight pass complete on shutdown; document the behavior (same page as §2 graceful-shutdown gap) |

## 7. The scaffold-compile break (verified end-to-end, 2026-07-03)

Reproduced with the HEAD CLI (`dotnet run --project src/Modulus.Cli -- init Sample --no-git`, then `dotnet build`). Three independent causes stack; all three must be fixed before the next CLI/package release. **This also means the E2E CI job (`ci.yml:100-126`, blocking) cannot currently pass at HEAD** — the "E2E passes" status predates the template/library co-evolution below.

**(a) Templates reference unpublished library APIs — High.**
`ApplicationServiceExtensions.cs.template` registers `UnitOfWorkBehavior<,>` and the `BaseDbContext` template implements `IUnitOfWork` — both exist only at HEAD. Every published `ModulusKit.Mediator` (1.0.1–1.2.5, binary-verified) lacks them → `CS0246` in every fresh scaffold.
*Evidence:* `src/Modulus.Templates/templates/init/building-blocks/application/DependencyInjection/ApplicationServiceExtensions.cs.template`, infrastructure `BaseDbContext.cs.template`; published `ModulusKit.Mediator 1.2.5` contains only Logging/Metrics/Validation/UnhandledException behaviors.
*Action:* ship the mediator release before/with the next CLI release, **and** make E2E consume locally-packed HEAD packages via a temp NuGet feed (`dotnet pack` → `--modulus-kit-version` + `RestoreSources`) so template/library drift can never hide behind published packages again.

**(b) `Program.cs.template` is missing `using {{RootNamespace}}.WebApi;` — High.**
Both generators emit into the host project's root namespace (`build_property.RootNamespace ?? AssemblyName` → e.g. `Sample.WebApi`; verified in HEAD source `src/Modulus.Generators/HandlerRegistrationGenerator.cs:274,282`, `ModuleRegistrationGenerator.cs:249`, in the published 1.2.5 package binary, and in the sample's emitted `obj/.../generated/.../GeneratedModuleRegistration.g.cs` — `namespace SampleApp.WebApi;`). Top-level statements live in the global namespace, so without that using the calls fail: reproduced `CS1061` on `AddModulusHandlers` / `AddAllModules` / `MapAllModuleEndpoints`. The sample's `using SampleApp.WebApi;` (`samples/SampleApp/src/SampleApp.WebApi/Program.cs:8`) is the exact manual workaround.
*Action:* add `using {{RootNamespace}}.WebApi;` to `src/Modulus.Templates/templates/init/host/Program.cs.template` (one line), plus a Templates.Tests assertion. Optionally consider emitting generated registrations into `Microsoft.Extensions.DependencyInjection` in a future generator major to make scaffolds using-proof.

**(c) Version pin bug feeds (a) — High.** See §5 row 1: the always-`1.0.0` pin rolls scaffolds forward to the *oldest* published packages, maximizing the API gap and adding NU1603 + NU1903 noise.

---

## Top-line summary

The three pillars are internally healthy: consistent API surface, disciplined visibility, strong CLI/mediator/analyzer/template test density, clean packaging metadata, correct DLQ topology at both brokers. The gaps concentrate at two seams:

1. **The publish seam** (install → scaffold → build): templates, generators, and libraries co-evolve at HEAD but scaffolds consume nuget.org — currently broken (§7). Fix the three causes, add the local-feed E2E, ship the 2.0 wave.
2. **The operate seam** (run → observe → operate): no health checks, no messaging metrics, no DLQ tooling/docs, undocumented shutdown, and one real correctness race in inbox dedup (§6).
