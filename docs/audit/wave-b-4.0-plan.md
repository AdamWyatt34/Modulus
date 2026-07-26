# Wave B — 4.0 Plan (Parked Breaking Work)

The 2026-07-25 full scan's "Missing features" list ([`full-scan-2026-07-25.md`](full-scan-2026-07-25.md) §Missing features) split into two compatibility waves. Wave A — the additive items — ships as **3.1.0** (see `CHANGELOG.md` `[Unreleased]`). This document parks the four **breaking** items for a coordinated **4.0.0**: none of them can ship in a 3.x minor because each changes a public contract, a schema shape, or default runtime behavior. Verified against `main` on 2026-07-26: none has partially landed.

Ordering below is by leverage; items 1 and 4A are the reasons 4.0 exists, 2 rides along for the TFM window, 3 is an opportunistic deletion while breaks are already allowed.

## 1. Multi-instance outbox competition (audit item 2)

**Today**: `EfOutboxStore.GetPending` is a plain fetch; nothing claims rows. Scaled-out replicas each run an `OutboxProcessor`, so the same pending row is fetched and published by every instance (duplicate publishes) and `MarkAsFailed`'s load-modify-save races `Attempts`. The docs work around it by recommending a single logical dispatcher.

**Plan**: EF-portable optimistic claim instead of raw `SKIP LOCKED` (the package is provider-agnostic; per-provider SQL would fork the store). Shape:

- `OutboxMessage` gains `ClaimedBy` (instance id) + `ClaimedUntil` (lease expiry, UTC) columns; polling index reshaped to cover the claim predicate.
- Claim pass = set-based `ExecuteUpdateAsync` stamping `(ClaimedBy, ClaimedUntil)` on up-to-batch-size rows `WHERE` unclaimed-or-lease-expired and eligible (same pending/backoff predicate as today), then fetch `WHERE ClaimedBy = me` — single-winner per row on every relational provider, no provider-specific hints.
- `MarkAsProcessed`/`MarkAsFailed` become claim-guarded (`WHERE ClaimedBy = me`) set-based updates so a lease takeover can't double-count attempts.
- Lease duration derives from the poll interval + dispatch budget; a crashed instance's rows free automatically at `ClaimedUntil`.

**Breaking**: `IOutboxStore` contract (claim semantics on `GetPending` or a new `ClaimPending` replacing it), `OutboxMessages` schema (two new columns + index reshape; consumer-owned migration, 3.0.0 precedent), and behavioral change for anyone relying on multi-reader fetch. Coordinate with the 3.1.0 retention/admin stores (`PurgeProcessedAsync` must not delete claimed-unprocessed rows — it already only touches `ProcessedAt != null`).

**Scope**: medium-large. Store + dispatcher + admin store + entity + context + ~4 test files (incl. a two-competing-dispatchers test, the coverage gap the audit called out) + `outbox-pattern.md` rewrite of the single-dispatcher warning + CHANGELOG migration note.

## 2. Multi-targeting `net8.0` + `net10.0` (audit item 5)

**Today**: `Directory.Build.props` pins single-TFM `net10.0`; every LTS-bound shop is excluded. The audit text says "net8.0/net9.0", but net9.0 (STS) left support in May 2026 — the sensible 4.0 set is **`net8.0;net10.0`** (both LTS). Decision for the release owner: drop net9.0 from the goal or carry it dead.

**Plan**:

- `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` in `Directory.Build.props` (analyzers/generators/code-fixes stay `netstandard2.0` via their existing per-csproj override).
- Central package management gets TFM-conditional pins: EF Core is the only hard fork (10.0.x has no net8 assets) → `Condition="'$(TargetFramework)'=='net8.0'"` rows pinning EF 8.0.x. Everything else already ships net8.0/netstandard2.0 assets (verified: M.E.* 10.x, System.CommandLine, Roslyn 4.14, FluentValidation, RabbitMQ.Client 7.x, Azure SDKs).
- CI: test matrix over both TFMs (the real risk is EF8-vs-EF10 behavioral drift in `Modulus.Messaging`, not compilation — no net9/net10-only APIs found in `src/`).
- Decisions: whether `ModulusKit.Cli` (a tool; has `RollForward=LatestMajor`) multi-targets, and whether scaffolds gain `--framework` or stay net10-only.

**Breaking**: packaging-surface change (nominally additive for consumers, but bundled into 4.0 so the EF-version fork and any behavior deltas land behind a major).

**Scope**: small-medium and mechanical; risk concentrated in running the full messaging suite against EF 8.

## 3. Inbox dead-API removal (audit item 13)

**Today**: `IInboxStore.GetPending`/`MarkAsProcessed` and `InboxMessage.ProcessedOnUtc` are dead surface — nothing calls them; the reservation model (`TryReserve`/`MarkConsumerProcessed`, stale-reservation takeover) already provides crash recovery, and the 3.1.0 retention sweep purges by `OccurredOnUtc` without needing them.

**Plan**: remove rather than build the recovery loop the schema implies (a reprocessor would need per-handler awareness `GetPending` doesn't have — it would be a redesign, not a completion). Delete the two members, their `EfInboxStore` implementations, `InboxMessage.ProcessedOnUtc`, the `{ProcessedOnUtc, OccurredOnUtc}` index (schema change, consumer-owned migration note), matching docs rows, and the two dead tests.

**Breaking**: `IInboxStore` custom implementors (3.0.0 precedent for exactly this kind of break) + inbox schema.

**Scope**: small; pure deletion.

## 4. Mediator: delegate cancellation token + publish strategies (audit item 14)

**4A — `RequestHandlerDelegate` cancellation token.** Today the delegate is parameterless, so a pipeline behavior cannot substitute its own token — timeout behaviors and linked-token patterns are impossible. Adopt the MediatR-12 shape:

```csharp
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);
```

Source-compatible for `await next()` call sites (default parameter), binary-breaking in Abstractions. `Mediator` threads the token through the pipeline chain instead of closure-capturing it; the six built-in behaviors change one line each (`next()` → `next(cancellationToken)`); custom behaviors recompile untouched.

**4B — Publish strategies.** `Publish` is hardcoded sequential/collect-errors. Add a strategy on mediator registration (e.g. `AddModulusMediator(o => o.PublishStrategy = PublishStrategy.Parallel)`) with `Sequential` (default, current), `Parallel` (`Task.WhenAll`), and `StopOnFirstFailure`. Mechanically additive, but grouped here so any default-behavior discussion happens behind a major, and because the strategy must live inside the runtime-type dispatch internals (`PublishInternal`), not the reflection shell. Cancellation semantics per strategy must be pinned by tests (3.0.0 made `Publish` stop-and-rethrow on cancellation — preserve that).

**Scope**: small-medium; Abstractions + Mediator + 6 behaviors + fixtures + 3 docs pages + 2 package READMEs; grep templates/skill for custom-behavior examples using `next()`.

## Sequencing into 4.0

1. Branch `release/4.0` off the 3.1.0 tag; land items in the order above (1 is the long pole; 2 last so the EF8 matrix validates the finished code).
2. Each item is one PR with its own CHANGELOG `### Changed`/`### Removed` entry under a `[4.0.0-preview]` heading, migration notes in the 3.0.0 style (consumer-owned migrations, interface-implementor call-outs).
3. The scaffolded `Directory.Packages.props` pins all nine packages to one version, so 4.0.0 is a coordinated release of the whole set regardless of which packages actually changed — same as 2.0.0/3.0.0.
