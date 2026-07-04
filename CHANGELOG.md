# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

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

[2.0.0]: https://github.com/adamwyatt34/Modulus/compare/v1.1.0...messaging-v2.0.0
[1.1.0]: https://github.com/adamwyatt34/Modulus/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/adamwyatt34/Modulus/releases/tag/v1.0.0
