# Strategy & Monetization Reference

## Contents
- ModulusKit monetization model
- Package adoption ladder
- Positioning against MediatR
- Value communication by package tier
- Future monetization paths
- Anti-patterns

---

## ModulusKit Monetization Model

ModulusKit is MIT-licensed OSS. Current monetization is indirect: the packages build reputation
and demonstrate architectural expertise. There is no paywall, no SaaS tier, and no commercial
license. The strategy is credibility-driven adoption.

This means "monetization" in this context is:
1. NuGet download growth → portfolio signal
2. GitHub stars → community credibility
3. Developer word-of-mouth → consulting/employment leverage

All messaging decisions should optimize for adoption and trust, not revenue conversion.

## Package Adoption Ladder

Developers typically adopt ModulusKit in tiers. Messaging must speak to each entry point:

**Tier 1 — Try the CLI** (lowest commitment)
```bash
# Zero code changes, evaluating the scaffold quality
dotnet tool install --global ModulusKit.Cli
modulus init EShop --aspire
```
Message: "See what you get in 60 seconds."

**Tier 2 — Adopt the Mediator** (committed to the architecture)
```xml
<PackageReference Include="ModulusKit.Mediator" />
```
Message: "Custom CQRS with no MediatR dependency. Drop it in, wire DI, done."

**Tier 3 — Add Messaging** (production-ready)
```xml
<PackageReference Include="ModulusKit.Messaging" />
```
Message: "Transactional outbox and reliable event delivery. Works with your existing EF Core context."

**Tier 4 — Full adoption** (all packages)
```xml
<PackageReference Include="ModulusKit.Generators" />
<PackageReference Include="ModulusKit.Analyzers" />
```
Message: "Source-generated handler registration and compile-time architecture enforcement."

README package table should reflect this ladder — list packages in adoption order, not
alphabetical order.

## Positioning Against MediatR

MediatR is the dominant alternative. Every developer evaluating ModulusKit.Mediator will ask
"why not just use MediatR?". NEVER ignore this question.

```markdown
<!-- GOOD — direct comparison, concrete tradeoffs -->
## Why Not MediatR?

MediatR is excellent. If you're already using it, keep using it.

ModulusKit.Mediator makes sense if you:
- Want zero external dependencies in your domain layer
- Need a Result pattern baked into the pipeline (not bolted on)
- Want the mediator owned by your codebase, not a third-party package

<!-- BAD — vague differentiation, forces reader to guess -->
## Our Mediator

ModulusKit includes a custom mediator implementation that provides an alternative to
third-party mediator libraries.
```

**Comparison table format** (for docs):

```markdown
| Feature | ModulusKit.Mediator | MediatR |
|---------|---------------------|---------|
| NuGet dependency | None | MediatR + MediatR.Extensions.DI |
| Result pattern | Built-in `Result<T>` | DIY or library |
| Streaming | `IStreamQuery<T>` | `IStreamRequest<T>` |
| Pipeline behaviors | Yes | Yes |
| Notifications/Events | Domain events | `INotification` |
```

## Value Communication by Package

Each package's README and `<Description>` must answer: "what does this save me from building myself?"

```xml
<!-- Abstractions packages — answer: "contracts I can depend on" -->
<Description>Contracts for ModulusKit mediator: ICommand, IQuery, IStreamQuery, Result<T>, Error.
Reference this in your Domain layer — zero infrastructure dependencies.</Description>

<!-- Implementation packages — answer: "the hard parts, already built" -->
<Description>CQRS mediator for .NET with UnhandledExceptionBehavior, LoggingBehavior,
ValidationBehavior, and MetricsBehavior. Wire with AddModulusMediator() in one line.</Description>

<!-- Generators — answer: "zero handler registration boilerplate" -->
<Description>Source generator for ModulusKit. Emits AddModulusHandlers() at compile time —
no manual handler registration, no reflection at startup.</Description>
```

## Future Monetization Paths

If commercial monetization becomes a goal, the natural paths for a developer tool are:

1. **Paid templates/recipes** — premium scaffold templates (Clean Architecture + CQRS + CQRS-ES)
2. **Support/consulting tier** — architecture review for teams adopting the toolkit
3. **Enterprise features** — stricter analyzers, audit logging, SOC2 compliance templates

Any of these would require a separate commercial package (`ModulusKit.Enterprise.*`) to keep
the OSS core free. NEVER add paywalls to the core library packages.

## WARNING: Overpromising Scope

**The Problem:**
```markdown
<!-- BAD — scope creep in positioning -->
ModulusKit is a complete solution for building enterprise .NET applications at any scale,
from startup monoliths to distributed systems.
```

**Why This Breaks:** "Any scale" and "distributed systems" is MassTransit + Dapr territory,
not a scaffolding CLI. Overpromising scope erodes trust when developers hit the actual
boundaries. The ICP is **modular monolith builders** — claim that clearly and stop there.

**The Fix:**
```markdown
<!-- GOOD — honest scope boundary -->
ModulusKit is designed for modular monolith architectures. If you're building microservices
from day one, it's probably not the right tool.
```
