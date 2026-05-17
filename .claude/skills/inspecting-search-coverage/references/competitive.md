# Competitive Reference — Benchmarking Against Similar Libraries

## Contents
- Competitor landscape
- Keyword gaps vs. competitors
- Differentiation messaging for metadata
- NuGet listing comparison

## Competitor Landscape

ModulusKit competes for developer attention against:

| Library | NuGet Package | Weekly Downloads | Primary Claim |
|---------|--------------|-----------------|---------------|
| MediatR | `MediatR` | ~15M | De-facto .NET mediator standard |
| Wolverine | `WolverineFx` | ~200K | In-process mediator + message bus |
| Brighter | `Paramore.Brighter` | ~100K | Command processor + outbox |
| Immediate.Handlers | `Immediate.Handlers` | ~50K | Source-generated handlers (Roslyn) |
| Mediator (NuGet) | `Mediator` | ~500K | Zero-reflection mediator via Roslyn |

ModulusKit's differentiator is the **integrated stack**: mediator + messaging + outbox + CLI scaffolding + compile-time analyzers — all in one coherent `ModulusKit.*` family.

## Keyword Gaps vs. Competitors

Searches where ModulusKit should rank but currently lacks coverage:

| Search Term | Competitor Ranking | ModulusKit Gap |
|-------------|-------------------|----------------|
| `dotnet mediator no mediatR` | `Mediator`, `Immediate.Handlers` | Missing "no MediatR" in any description |
| `dotnet modular monolith scaffold` | None (opportunity) | CLI not tagged `scaffold;template;code-gen` |
| `transactional outbox dotnet` | `Wolverine`, `Brighter` | Messaging package lacks `transactional-outbox` tag |
| `cqrs result pattern dotnet` | None dominant | Result pattern not in abstractions description |
| `dotnet aspire modular monolith` | None (opportunity) | Aspire support not surfaced in tags |
| `roslyn analyzer architecture` | `NetArchTest` | Analyzer description too thin |

## Differentiation Messaging for Package Descriptions

### vs. MediatR

```xml
<!-- Lead with the MediatR alternative angle -->
<Description>CQRS mediator for .NET without MediatR. Compile-time handler registration via Roslyn source generators, built-in Result pattern, pipeline behaviors, and FluentValidation integration — zero reflection at request dispatch time.</Description>
```

### vs. Wolverine/Brighter (Messaging)

```xml
<!-- ModulusKit.Messaging — differentiate on integrated outbox + CLI -->
<Description>MassTransit abstraction for .NET modular monoliths with transactional outbox/inbox pattern. Supports RabbitMQ, Azure Service Bus, and in-memory transports. Pairs with ModulusKit.Mediator for a complete in-process + cross-process messaging solution.</Description>
```

### vs. Immediate.Handlers (Generator)

```xml
<!-- ModulusKit.Generators — differentiate on full ecosystem, not just handlers -->
<Description>Roslyn incremental source generator for ModulusKit. Emits AddModulusHandlers() at compile time, auto-discovering all ICommandHandler, IQueryHandler, IDomainEventHandler, IIntegrationEventHandler, and FluentValidation validators — no reflection, no runtime scanning.</Description>
```

## GitHub Repository Discoverability

GitHub topic search is independent of NuGet. The repository at `github.com/adamwyatt34/Modulus` should have:

**Recommended GitHub Topics:**
```
dotnet cqrs mediator modular-monolith outbox-pattern masstransit roslyn source-generator
dotnet-tool aspire result-pattern fluent-validation pipeline-behavior
```

Set via GitHub repository Settings → General → Topics. This surfaces the repo in GitHub Explore under each topic.

**Repository Description** (shown on GitHub search results):
```
ModulusKit — CQRS mediator, transactional outbox, and CLI scaffolding for .NET modular monoliths. 7 NuGet packages, Roslyn source generators, compile-time analyzers.
```

## WARNING: Package Family Not Cross-Linked

**The Problem:**
Each ModulusKit package README mentions only its own package. A developer who finds `ModulusKit.Mediator` doesn't know that `ModulusKit.Generators` eliminates DI registration boilerplate.

**Why This Breaks:**
- Developers adopt only one package and miss the integrated value
- Package install counts remain low on companion packages
- Perceived as a single mediator library, not a modular monolith ecosystem

**The Fix:**
Each package README should end with:

```markdown
## The ModulusKit Family

| Package | Purpose |
|---------|---------|
| `ModulusKit.Mediator.Abstractions` | CQRS interfaces + Result types |
| `ModulusKit.Mediator` | DI registration + pipeline behaviors |
| `ModulusKit.Generators` | Compile-time handler auto-registration |
| `ModulusKit.Analyzers` | Architecture rule enforcement (MOD001-MOD005) |
| `ModulusKit.Messaging.Abstractions` | Integration event interfaces |
| `ModulusKit.Messaging` | MassTransit + transactional outbox/inbox |
| `ModulusKit.Cli` | `dotnet tool install -g ModulusKit.Cli` scaffold tool |
```

See the **crafting-page-messaging** skill for cross-link copy and the **structuring-offer-ladders** skill for positioning the package family as a tiered offering.
