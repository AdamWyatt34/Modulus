# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

Feature wave from the 2026-07-25 full audit's "Missing features" list (`docs/audit/full-scan-2026-07-25.md`). Additive only — no breaking API changes from 3.0.0 (verified mechanically by the new package-validation baseline). **One action required on upgrade**: the messaging schema gains nullable columns (`OutboxMessages.TraceParent`/`TraceState`/`ScheduledOnUtc`, a reshaped outbox polling index, and an `InboxMessages.OccurredOnUtc` index) — outbox polling queries reference the new columns immediately, so generate and apply the consumer-owned follow-up migration **before** deploying 3.1.0 binaries (`src/Modulus.Messaging/Migrations/README.md`); this applies equally to application contexts that map the outbox table themselves.

### Added

- **Trace-context propagation across the broker**: `TransportEnvelope` gains a string `Headers` bag mapped to each broker's native mechanism (RabbitMQ AMQP headers, Azure Service Bus application properties), carrying W3C `traceparent`/`tracestate`. `IMessageBus.Publish` runs under a Producer span (`Modulus.Messaging` ActivitySource) and injects its context; the consumer pipeline starts a Consumer span per delivery parented on the extracted context (tags: `modulus.message_id`, `modulus.message_type`, `modulus.outcome`, `modulus.attempt`; dead-letters set error status), and `Activity.Current` flows into handlers. The outbox persists the saving request's context in new nullable `OutboxMessages.TraceParent`/`TraceState` columns (**consumer-owned migration required** — see `src/Modulus.Messaging/Migrations/README.md`); `outbox.dispatch` spans link to it via `ActivityLink` and inject their own context into the envelope. ASB consumers honor the Azure SDK's `Diagnostic-Id` as a `traceparent` fallback for non-Modulus publishers. New public `MessagingDiagnostics` constants for OTel wiring; new docs page `docs/messaging/distributed-tracing.md`.
- **Broker-native delayed redelivery**: opt-in `MessagingOptions.ConsumerRetryMode = ConsumerRetryMode.Broker` moves consumer retry backoff onto the broker — one handler pass per delivery, then the transport schedules a delayed copy (attempt count in a `modulus-delivery-attempt` header) and consumes the original, so a failing message no longer pins a concurrency slot for the whole retry budget (~53 s at the defaults) and the backoff survives a crash. RabbitMQ uses a per-endpoint `{queue}.retry` TTL queue that dead-letters straight back into the work queue; Azure Service Bus schedules a copy onto the topic with a `modulus-redeliver-endpoint` property (foreign endpoints acknowledge it unrun); the in-memory transport uses timers, so `Broker` mode behaves identically in tests. The failed pass's inbox reservation is released so the redelivery executes immediately. New `MessageDispatchResult.Retry` member; custom transports that don't handle it should stay on the default `InProcess` mode.
- **Scheduled publishing**: new `IMessageBus.PublishScheduled(@event, enqueueAtUtc)` (ASB native scheduling; RabbitMQ per-event-type `{exchange}.scheduled` TTL queue; in-memory timer) and a durable outbox route — `IOutboxStore.Save(@event, enqueueAtUtc)` stamps a new nullable `OutboxMessages.ScheduledOnUtc` column that gates dispatch *and* the backlog count, so far-future messages never trip the backlog health check (**consumer-owned migration required**: new column + polling index becomes `{ProcessedAt, NextAttemptOnUtc, ScheduledOnUtc, CreatedAt}`). Both new interface members ship as default implementations that throw `NotSupportedException`, so existing custom buses/stores keep compiling. `TransportEnvelope` gains `ScheduledEnqueueTimeUtc`.
- **Outbox/inbox retention & cleanup**: opt-in `MessagingOptions.Retention` runs a background sweep (`SweepInterval`, default hourly) that bulk-deletes outbox rows published more than `ProcessedOutboxAge` ago and inbox rows older than `InboxAge` (both default 7 days), in `PurgeBatchSize` batches until drained — bounding the table growth that would otherwise degrade the polling and reservation queries forever. Unprocessed and dead-lettered outbox rows are never touched. New bulk admin APIs back it: `IOutboxAdminStore.CountProcessedAsync`/`PurgeProcessedAsync` (default interface implementations throw `NotSupportedException`, so existing custom stores keep compiling) and a new `IInboxAdminStore` registered by `AddModulusInbox`. New `modulus outbox purge-processed` and `modulus inbox purge` CLI commands preview the matching row count until `--confirm` is passed. New `modulus.messaging.retention.purged` counter (tag `store`: `outbox`/`inbox`). Inbox purging shortens the deduplication window — size `InboxAge` past the broker's maximum redelivery horizon (docs cover the trade-off).
- **Package hygiene guards**: pack-time `EnablePackageValidation` on every packable project with a 3.0.0 `PackageValidationBaselineVersion` for the library packages (packing now mechanically proves each release is binary-compatible with the last — this wave's additive claim is machine-checked, not asserted); `Microsoft.CodeAnalysis.PublicApiAnalyzers` with populated `PublicAPI.Shipped.txt` baselines across the nine library packages, so accidental public-API changes fail CI; weekly grouped Dependabot (nuget + github-actions); a CodeQL C# workflow; and NuGet **trusted publishing** — the publish job mints a short-lived API key via OIDC (`NuGet/login`, gated on a `NUGET_USER` repository variable) with the legacy `NUGET_API_KEY` secret as fallback until the nuget.org policy is configured (setup in CONTRIBUTING).
- **`ModulusKit.Testing` package** (new, tenth package; tag prefix `testing-v`): module-level integration testing without hand-rolling a fake transport or raw outbox/inbox queries. `TestMessageTransport` is a public `IMessageTransport`/`ITransportHealthProbe` built entirely against the public transport SPI that mirrors the in-memory transport's delivery — scheduled publishes and `Retry` redelivery included — while adding a thread-safe `Published`/`DeadLettered` envelope history, `PublishFailure` injection, and typed `PublishedEventsOf<TEvent>()`/`DeadLetteredEventsOf<TEvent>()` helpers; `AddModulusTestTransport()` swaps it in after `AddModulusMessaging`. `ModulusMessagingTestHarness` runs registered hosted services with real host ordering (consumer host before outbox processor, reverse on shutdown). `TestWait` ports the library's polling-assertion helper, and `OutboxTestQueries`/`InboxTestQueries` provide assertion-library-agnostic outbox/inbox queries (pending, dead-lettered, drain-wait, per-handler processed). Scaffolded `Tests.Integration` projects now reference the package; new docs page `docs/testing/modulus-testing.md`.
- **Run-grade E2E**: the CLI E2E suite now *runs* what it scaffolds instead of stopping at `dotnet build` — a full chain (`init` → `add-module` → `add-entity`/`add-command`/`add-query`/`add-endpoint`, generic property/result types and a typed route parameter included) boots the built WebApi on an ephemeral port, asserts `/healthz`, `/readyz`, and the module's sample endpoint over real HTTP, passes `doctor --strict`, and runs the scaffolded test suite (module integration tests excluded — they need Docker). This is the harness the audit said would have caught the never-loaded-module and non-compiling-scaffold criticals by construction.
- **CLI — `modulus add-migration`**: adds an EF Core migration for a module's DbContext by wrapping `dotnet ef migrations add` with the solution's own layout — `--project` inferred as the module's Infrastructure project, `--startup-project` as the WebApi host (whose generated `AddAllModules` registers the context, so no `IDesignTimeDbContextFactory` is needed and no database is contacted), `--context` defaulting to `{Module}DbContext`, plus `--output-dir` and `--dry-run`. A failed run prints the dotnet-ef install hint and the exact invocation for manual re-run. Scaffolds now reference `Microsoft.EntityFrameworkCore.Design` in the WebApi host (design-time assets only) so `dotnet ef` works out of the box; new docs page `docs/cli/add-migration.md` covers the read-only-context and messaging-tables caveats.
- **CLI — universal `--dry-run`**: every mutating scaffold command (`init`, `add-module`, `add-entity`, `add-command`, `add-query`, `add-endpoint`, `add-event`, `add-consumer`) now accepts `--dry-run`, printing every file that would be created and every edit/process invocation (host `ProjectReference` wiring, `dotnet sln add`, `dotnet restore`, git init/add/commit) that would happen, then exiting `0` without touching the filesystem or spawning any process.
- **CLI — generic types in `--result-type`/`--properties`**: type references now accept arbitrarily nested generics (`List<T>`, `Dictionary<K,V>`, `PagedResult<OrderSummaryDto>`), nullable types (`Guid?`), and array ranks (`int[]`, `int[][]`, `int[,]`); `--properties` splits on top-level commas only, so nested generic argument lists no longer mis-split. Unbalanced brackets, empty generic argument lists, and reserved keywords are still rejected with clear errors.
- **CLI — route-parameter binding in `add-endpoint`**: `{param}`/`{param:constraint}`/`{param:constraint?}` segments in `--route` are typed from their constraint (unconstrained → `string`), bound as leading parameters on the generated minimal-API lambda, and forwarded positionally into the wired command/query constructor across GET/POST/PUT/DELETE and the bare stub; the `201 Created` `Location` header interpolates the real bound value. Invalid, duplicate, or colliding parameter names are rejected up front.
- **CLI — `--no-restore`** on `init` and `add-module` skips the post-scaffold `dotnet restore` for CI/scripted use (`add-module` still runs `dotnet sln add` — solution wiring, not restore).
- **CLI — CI workflow and Dockerfile scaffolds**: `modulus init --ci github` emits `.github/workflows/ci.yml` (restore/build/test on ubuntu-latest, major-tag-pinned actions, `permissions: contents: read`); `modulus init --dockerfile` emits a multi-stage `Dockerfile` (SDK build → ASP.NET runtime) plus `.dockerignore` targeting the scaffolded WebApi. Both opt-in, neither emitted by default. (The audit's `dotnet new` template-package idea is deferred: the runtime token-replacement template engine is incompatible with `dotnet new`'s static substitution model — see `docs/audit/missing-features-plan.md`.)
- **Strongly-typed IDs completed**: `[StronglyTypedId]` supports a `string` backing type (null-validating constructor, no `New()` factory, `Empty => string.Empty`; MODGEN005 no longer fires for `string`); every generated ID implements `IComparable<TId>` and `IParsable<TId>` with static `Parse`/`TryParse`, so IDs bind directly as minimal-API route and query parameters; the generated `JsonConverter` overrides `ReadAsPropertyName`/`WriteAsPropertyName`, so `Dictionary<TId, TValue>` keys serialize instead of throwing; and — gated on the same EF Core reference check as the per-ID `ValueConverter` — the generator emits `ModulusStronglyTypedIdConventions.UseModulusStronglyTypedIds(this ModelConfigurationBuilder)`, one `ConfigureConventions` call that registers every discovered ID's converter (local declarations plus public IDs from referenced assemblies built with EF Core) instead of per-property `HasConversion<>()`.
- **Result combinators**: `Result` and `Result<T>` gain `Bind`, `Map`, `Tap`, `Ensure`, `BindAsync`, `MapAsync`, `TapAsync`, `MatchAsync`, and a `FirstError` property (first error of a failed result; throws on success like `Value` does on failure). A new `ResultExtensions` class provides the same combinators over `Task<Result>`/`Task<Result<T>>` sources, so handler chains compose fluently — `await LoadOrder(id).Ensure(o => o.IsOpen, error).Bind(o => Ship(o)).Map(o => o.Id)` — instead of nesting `if (r.IsFailure)` blocks. Failure short-circuits: later steps never run and errors propagate unchanged.

## [3.0.0] - 2026-07-25

Coordinated release of all nine packages at 3.0.0 — the fix wave from the 2026-07-25 full audit (`docs/audit/full-scan-2026-07-25.md`). Major because of the interface and schema changes to the messaging stores (see Changed); everything else is bug fixes.

### Fixed

- **Mediator — domain events were silently dropped**: `Publish` now dispatches on the event's *runtime* type (like `Send`/`Query`/`Stream`), so publishing through an `IDomainEvent`-typed variable — the scaffolded `BaseDbContext` pattern — reaches handlers instead of resolving zero. Generic-dispatch caches are keyed per result type, fixing wrong-handler dispatch for types implementing two closed `IQuery<>`s.
- **CLI — scaffolded modules never loaded**: `add-module` now adds the `ProjectReference` from the host WebApi to the module's Infrastructure project (module discovery is compile-time over host references); `remove-module` removes it and blocks on references from the host, root tests, and BuildingBlocks, not just sibling modules.
- **Outbox — poison rows and hot loops**: unknown-type/undeserializable rows are marked failed (visible to `modulus outbox list-failed`, dead-lettered at `MaxAttempts`) instead of silently wedging the head of every poll batch; the event-type allowlist matches assembly-qualified names version-insensitively, so a CI version bump can no longer orphan in-flight rows; failed rows back off per `RetryPolicy` (previously dead config for the outbox) instead of hot-looping the processor when a full batch fails.
- **Outbox — lost wake signal**: `OutboxNotifyingInterceptor` now signals for saves under EF's *implicit* save transaction (commit fires before `SavedChanges`), restoring 2.1.0's immediate dispatch for the canonical business-row+outbox-row save on an outbox-mapping context.
- **Inbox — cross-module handler dedup**: idempotency keys on the handler's `FullName`; two modules each defining e.g. `OrderPlacedHandler` no longer deduplicate each other (previously one silently never ran). Dead-lettering releases the dispatch's own reservation so a prompt `modulus dlq replay` executes instead of re-dead-lettering; `EfInboxStore.Save` only swallows `DbUpdateException` for genuine duplicates.
- **Strongly-typed IDs**: the EF Core `ValueConverter` is generated only when the compilation references EF Core — Domain projects compile without an EF dependency; hint names are namespace-qualified (same-named IDs no longer fault the generator); generated partials mirror declared accessibility; `TypeConverter` parses invariantly.
- **Generators**: referenced-assembly handler scan skips internal/value-type handlers (previously emitted uncompilable registrations); registrations are de-duplicated; a legal partial-class shape no longer faults the whole generator; pipeline models are value-equatable (incremental caching actually caches); both generators pre-filter referenced assemblies instead of walking the entire closure on every keystroke; module discovery keeps `global::`, validates method signatures before emitting calls, and finds modules in the host assembly.
- **Analyzers**: MOD001's `BuildingBlocks` exemption requires a name boundary and `global::` usings no longer bypass MOD001/MOD004; MOD002 resolves the actual interface implementation; MOD003 analyzes `record` handlers; the MOD003 code fix compiles in non-async handlers, skips throw expressions, and stringifies non-string exception arguments.
- **Transports**: concurrent first-publish no longer races an unsynchronized set (RabbitMQ) or client init (ASB); RabbitMQ `StopConsumingAsync` drains in-flight handlers (bounded 30s) so deploys stop producing guaranteed redeliveries; acks target the delivering channel; replaced connections/channels are disposed; health probes report dead consumers (RabbitMQ channel/consumer faults; ASB real namespace probe + missing-entity detection) instead of staying green forever; ASB settlement uses `CancellationToken.None` (shutdown can't abandon processed messages) and provisioning races (`MessagingEntityAlreadyExists`) are treated as success; nested event types produce legal ASB topic names; entity-name length limits fail fast with clear errors.
- **CLI scaffold output compiles and passes out of the box**: `add-endpoint` no longer emits an unresolvable `using` or brace-broken `Results.Created`; `--result-type` accepts C# aliases (`string`, `int`, …); the scaffolded module integration test targets `/api/{module}/sample` (and is omitted under `--no-endpoints`); entity unit tests no longer compare two different `Guid.NewGuid()` values; `--aspire` actually injects `AddServiceDefaults()`/`MapDefaultEndpoints()`; `AuditableEntityInterceptor` is registered; `AllowedHosts` defaults to `*`; scaffolds pin `Microsoft.OpenApi` against GHSA-v5pm-xwqc-g5wc with transitive pinning; module projects reference `ModulusKit.Analyzers`/`ModulusKit.Generators`, so boundary rules and ID generation run where module code lives.
- **CLI operations**: `modulus outbox` connects to the outbox database (`ConnectionStrings:Default`), not the broker string; DLQ listing reports real delivery counts (`x-death`) and timestamps, ASB replay no longer rescans the same head messages; `doctor` ignores commented-out registrations, recognizes Aspire scaffolds, and skips MSBuild-variable includes; `upgrade` validates `--version`; `init` reports git failures honestly; ambiguous/missing solution paths get clear errors.
- **Packaging/docs**: symbol packages (`.snupkg`) are actually uploaded and published; code fixes ship in a separate `Modulus.Analyzers.CodeFixes` assembly so the analyzer loads cleanly in command-line builds (RS1038); analyzer/generator packages ship their READMEs; MIT `LICENSE` added at the repo root; docs rewritten to match the real scaffold layout and the honest outbox transactionality story; VitePress nav/link fixes; duplicate project entries removed from `SampleApp.slnx`.

### Changed

- **BREAKING — `IOutboxStore.MarkAsFailed`** gains a `DateTime? nextAttemptOnUtc` parameter; custom implementations must add it.
- **BREAKING — `IInboxStore`** gains `ReleaseReservation(Guid, string, CancellationToken)`; custom implementations must add it.
- **BREAKING — schema**: `OutboxMessages` gains nullable `NextAttemptOnUtc` and the polling index becomes `{ProcessedAt, NextAttemptOnUtc, CreatedAt}`. Consumer-owned migrations: run `dotnet ef migrations add` for the outbox context after upgrading.
- **BREAKING — inbox key migration**: consumer tracking switched from handler simple name to `FullName`. A row already marked processed under the old key looks unprocessed under the new key, so one redelivered in-flight message per handler may re-execute once during rollout.
- **Behavioral**: `Result<T>.Success(null)` and the implicit null conversion now throw `ArgumentNullException` (previously a "successful" result with no value); zero-error failures throw `ArgumentException`; `UnhandledExceptionBehavior` propagates `OperationCanceledException` instead of converting it to a failed `Result`, and `Publish` stops dispatching and rethrows on cancellation; unsupported `[StronglyTypedId]` backing types are now a MODGEN005 error instead of silently generating a Guid ID (nested declarations: MODGEN006); duplicate integration-event `FullName`s throw at registration instead of silently cross-wiring modules; ASB processor prefetch is 0 and `MaxAutoLockRenewalDuration` scales with `ConsumerRetry`; the CLI tool rolls forward across majors (`RollForward=LatestMajor`).

## [2.1.0] - 2026-07-04

Coordinated release of all nine packages at 2.1.0 (the scaffolded `Directory.Packages.props` pins every `ModulusKit.*` package to one version). Additive only — no breaking changes from 2.0.0.

### Added

- **Messaging — immediate outbox dispatch (change notification)**: new outbox rows now wake the `OutboxProcessor` the moment they commit instead of waiting out `OutboxPollInterval`, cutting dispatch latency from seconds to milliseconds with no new infrastructure. Polling remains as the fallback sweep, so delivery guarantees are unchanged in every topology.
  - New public `IOutboxNotifier` singleton (`Notify()` / `WaitAsync`) — coalescing auto-reset wake signal; also the extension point for external change-data-capture listeners (e.g. a PostgreSQL `LISTEN/NOTIFY` hosted service calling `Notify()`).
  - New public `OutboxNotifyingInterceptor` (EF Core `ISaveChangesInterceptor` + `IDbTransactionInterceptor`): detects `OutboxMessage` inserts and signals when they become visible — at commit time inside EF-managed transactions (rollback never signals). Auto-attached to `OutboxDbContext` by `AddModulusOutbox`; attach to your own outbox-mapping context with `options.AddInterceptors(sp.GetRequiredService<OutboxNotifyingInterceptor>())`.
  - `IOutboxStore.Save` (EF implementation) signals directly when saving outside a transaction.
  - `OutboxProcessor` loop is now drain-then-wait: a full fetched batch re-dispatches immediately (backlog draining); otherwise it waits for a signal with `OutboxPollInterval` as the timeout. `OutboxPollInterval` is therefore a fallback knob — raising it (e.g. to 30s) cuts idle database queries without adding latency for signaled rows.
  - New `modulus.messaging.outbox.wakeups` counter (tag `reason`: `signal`/`poll`/`backlog`) shows whether a deployment actually receives change notifications or is running poll-only.
  - Scaffolded module DbContexts come pre-wired with the interceptor (no-op until messaging is registered).
  - The in-process signal wakes the instance that wrote the row; replicas, dedicated-worker topologies, external writers, and transactions EF Core does not observe (ambient `TransactionScope`, externally-owned `UseTransaction`) fall back to the poll sweep.

## [2.0.0] - 2026-07-03

Coordinated release of all nine packages at 2.0.0 — the scaffolded `Directory.Packages.props` pins every `ModulusKit.*` package to one version, so the set moves together. First release under the per-package tag scheme, and the first release of the two transport packages.

### Removed

- **BREAKING — Messaging**: `IMessageBus.Send` (both overloads) and the transport-level point-to-point path (`IMessageTransport.SendAsync`) are gone. Modulus never ran a consuming pipeline for commands, so the API implied wiring that didn't exist. Use integration events for cross-module communication, or direct broker access for queues owned by external services.

### Changed

- **BREAKING — Messaging inbox**: consumption is now reservation-based. `IInboxStore` replaces `RecordConsumer` with `TryReserve`/`MarkConsumerProcessed`, and `InboxMessageConsumers` gains `ReservedOnUtc` and `ProcessedOnUtc` columns (schema change — update your migrations). Concurrent duplicate deliveries now execute each handler exactly once; a crashed consumer's reservation goes stale after `MessagingOptions.ConsumerReservationTimeout` (default 5 minutes) and is taken over by a redelivery or dead-letter replay. `EfInboxStore` requires a relational EF Core provider.
- **BREAKING — Messaging (`Modulus.Messaging`)**: MassTransit has been replaced with an in-house transport layer to remove the last commercially licensed dependency.
  - New packages: `ModulusKit.Messaging.RabbitMq` (RabbitMQ.Client) and `ModulusKit.Messaging.AzureServiceBus` (Azure.Messaging.ServiceBus). Broker transports need one extra registration: `AddModulusRabbitMqTransport()` / `AddModulusAzureServiceBusTransport()`. The in-memory transport remains built into `ModulusKit.Messaging`.
  - Wire format and topology names are **not** MassTransit-compatible — drain queues before upgrading and delete old MassTransit exchanges/queues/topics afterwards. See `docs/messaging/migrating-from-masstransit.md`.
  - All registered handlers for an event are now invoked (previously only the last-registered handler ran).
  - New `MessagingOptions`: `EndpointName`, `PrefetchCount`, `AutoProvision`.
  - Consumer retry is in-process exponential backoff followed by transport dead-lettering; the delay curve approximates (is not identical to) MassTransit's `Exponential`.
  - The transitive `Newtonsoft.Json` pin is gone; serialization is System.Text.Json end to end.

### Added

- **Mediator**: opt-in `TracingBehavior` (ActivitySource `Modulus.Mediator` with request/outcome/error tags) alongside the existing `MetricsBehavior`; library-provided `UnitOfWorkBehavior` + `IUnitOfWork` (commit-on-successful-command via `SaveChangesAsync`, no-op when unregistered).
- **CLI `modulus doctor`**: six solution-health checks with `--json`/`--strict` and exit codes 0/1/2.
- **CLI `modulus remove-module`**: dry-run by default, `--confirm` to apply, `--force` to override cross-module reference blocking.
- **CLI `modulus add-event` / `add-consumer`**: integration event and handler scaffolding with cross-module Integration-only reference auto-wiring.
- **CLI `list-events` / `list-consumers` / `list-entities`**: convention-scan listings of scaffolded artifacts per module; all four list commands (including `list-modules`) support `--json`.
- **CLI `modulus dlq list|replay`**: inspect and replay broker dead-letter queues for RabbitMQ (`{endpoint}.dead-letter`, confirm-then-ack replay) and Azure Service Bus (subscription DLQ, clone-and-resend). Replayed messages keep their EventId, so the inbox re-runs only handlers that never succeeded. `RabbitMqTopology` and `AzureServiceBusTopology` are now public for tooling.
- **CLI `modulus upgrade`**: bumps all `ModulusKit.*` pins in `Directory.Packages.props` to a target version (default: the CLI's own version) with `--dry-run` support, preserving file formatting.
- **Scaffolding**: Aspire templates moved to Aspire 13.4.6 with the correct AppHost shape (`Aspire.AppHost.Sdk` + `Aspire.Hosting.AppHost`; the previously referenced `Aspire.Hosting.Defaults` package does not exist on nuget.org, so `--aspire` scaffolds could not restore). ServiceDefaults package pins refreshed.
- **Messaging metrics**: new `Modulus.Messaging` meter — outbox dispatch counter (outcome-tagged), consumer handler duration histogram, inbox dedup counter, consumer retry and dead-letter counters. Subscribe with `AddMeter("Modulus.Messaging")`; works without metrics DI.
- **Messaging health checks**: `AddHealthChecks().AddModulusMessaging()` registers a broker connectivity check (via the new optional `ITransportHealthProbe` on `IMessageTransport` implementations) and an outbox backlog-depth check with configurable degraded/unhealthy thresholds, both tagged `ready`/`messaging`. `IOutboxStore` gains `CountPending` (breaking for custom implementations); `ModulusKit.Messaging` now depends on `Microsoft.Extensions.Diagnostics.HealthChecks`. Scaffolded hosts filter `/readyz` on the `ready` tag.
- `IOutboxDispatcher` extraction from `OutboxProcessor` (single synchronous dispatch pass, used by tests and tooling).
- RabbitMQ Testcontainers integration test suite (`Category=Integration`); the CI job now **blocks publishing** and covers roundtrip, dead-lettering, inbox dedup, unknown-type acknowledge, consume restart, and `AutoProvision=false` against pre-declared topology.
- Azure Service Bus **emulator** integration test suite (Testcontainers, official emulator + SQL companion; `AutoProvision=false` with a checked-in `Config.json` pinned to the topology helpers by a drift-guard test) — non-blocking CI job until proven stable.
- Unit coverage for the hosted services: `OutboxProcessor` poll loop (repetition, exception resilience, prompt cancellation) and `TransportConsumerHost` lifecycle (publish-only early return, subscription forwarding, stop).

## [1.1.0] - 2026-03-05

### Added

- **Source Generators (`Modulus.Generators`)**
  - Strongly Typed ID source generator with Guid, int, and long backing type support
  - Handler and validator DI registration source generator (replaces Scrutor runtime scanning)
  - Module auto-discovery source generator (eliminates manual composition root)

- **Roslyn Analyzers (`Modulus.Analyzers`)**
  - MOD001: Module boundary violation (Error)
  - MOD002: Handler not returning Result/Result&lt;T&gt; (Warning)
  - MOD003: Throwing exceptions instead of returning Error in handlers (Warning)
  - MOD004: Infrastructure attributes in Domain layer (Warning)
  - MOD005: Public setter on entity property (Info)
  - Code fixes for MOD003 (exception to Result conversion) and MOD005 (public to private setter)

- **Attributes (`Modulus.Mediator.Abstractions`)**
  - `[StronglyTypedId]` attribute for compile-time ID type generation
  - `[ModuleOrder]` attribute for controlling module initialization order

### Changed

- Scrutor is no longer a required dependency (replaced by handler registration source generator)
- `ModuleRegistration.cs` is no longer generated by CLI (replaced by module auto-discovery generator)
- `modulus add-module` simplified -- no longer modifies composition root file
- `AddModulusMediator()` no longer requires assembly parameters

## [1.0.0] - 2026-02-28

### Added

- **CLI Tool (`Modulus.Cli`)**
  - `modulus init` command to scaffold a new modular monolith solution
  - `modulus add-module` command to add feature modules with full layer structure
  - `modulus list-modules` command to list all modules in a solution
  - `modulus version` command to display the CLI version
  - `--aspire` flag for .NET Aspire AppHost and ServiceDefaults integration
  - `--transport` flag to configure messaging transport (InMemory, RabbitMQ, Azure Service Bus)
  - `--no-git` flag to skip git initialization
  - `--no-endpoints` flag to create modules without an API layer

- **Mediator (`Modulus.Mediator` + `Modulus.Mediator.Abstractions`)**
  - CQRS mediator with `ICommand`, `IQuery`, `IStreamQuery`, and `IDomainEvent` support
  - `Result` and `Result<T>` types with typed `Error` values
  - `ValidationResult` for FluentValidation integration
  - Configurable pipeline behaviors (`IPipelineBehavior<TRequest, TResponse>`)
  - Built-in `ValidationBehavior` for automatic FluentValidation execution
  - Built-in `LoggingBehavior` for request timing and outcome logging
  - Built-in `UnhandledExceptionBehavior` for exception-to-Result conversion
  - Assembly scanning via Scrutor for automatic handler registration

- **Messaging (`Modulus.Messaging` + `Modulus.Messaging.Abstractions`)**
  - `IMessageBus` abstraction for publishing integration events and sending commands
  - `IntegrationEvent` base record with `EventId`, `OccurredOn`, and `CorrelationId`
  - MassTransit integration with pluggable transports (InMemory, RabbitMQ, Azure Service Bus)
  - Transactional outbox pattern with `IOutboxStore` and `OutboxProcessor`
  - Entity Framework Core outbox implementation (`EfOutboxStore`)
  - Automatic handler discovery and consumer adapter registration

[3.0.0]: https://github.com/adamwyatt34/Modulus/compare/messaging-v2.1.0...messaging-v3.0.0
[2.1.0]: https://github.com/adamwyatt34/Modulus/compare/messaging-v2.0.0...messaging-v2.1.0
[2.0.0]: https://github.com/adamwyatt34/Modulus/compare/v1.1.0...messaging-v2.0.0
[1.1.0]: https://github.com/adamwyatt34/Modulus/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/adamwyatt34/Modulus/releases/tag/v1.0.0
