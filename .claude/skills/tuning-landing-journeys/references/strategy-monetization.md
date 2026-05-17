# Strategy and Monetization Reference

## Contents
- ModulusKit's Monetization Model
- Value Ladder and Package Tiers
- Positioning Strategy
- Anti-Patterns

---

## ModulusKit's Monetization Model

ModulusKit is MIT-licensed open source distributed via NuGet. There is no direct revenue from package installs. Monetization paths that fit this model:

| Path | Mechanism | Readiness |
|------|-----------|-----------|
| **Consulting / Workshops** | "I built ModulusKit, hire me to architect your modular monolith" | Available now — requires bio + contact on docs site |
| **Sponsorship (GitHub Sponsors / OpenCollective)** | Companies using ModulusKit fund maintenance | Requires GitHub Sponsors setup |
| **Pro Tier (future)** | Closed-source add-ons (enterprise analyzers, VS extension, cloud scaffolding) | Not built — speculative |
| **Training / Courses** | "Modular Monolith with ModulusKit" video course | Not built — content exists in docs |

The current landing journey should not try to monetize directly. The goal is download count and developer trust. Monetization signals (consulting CTA, sponsor link) belong in low-friction positions: README footer, docs site footer.

## Value Ladder and Package Tiers

ModulusKit's package hierarchy is a natural adoption ladder. The landing journey should make this ladder explicit so developers know the minimal entry point:

```
Tier 1 — Abstractions Only (zero runtime overhead)
  ModulusKit.Mediator.Abstractions
  ModulusKit.Messaging.Abstractions
  → "Start here if you're building your own mediator"

Tier 2 — Full Implementation (recommended)
  ModulusKit.Mediator
  ModulusKit.Messaging
  → "Start here for the full CQRS + messaging stack"

Tier 3 — Compile-Time Tooling (multiplier)
  ModulusKit.Generators
  ModulusKit.Analyzers
  → "Add after Tier 2 for auto-registration and architecture enforcement"

Tier 4 — CLI (project scaffolding)
  ModulusKit.Cli
  → "Start here to scaffold a full solution from scratch"
```

This ladder should appear on `docs/index.md` or `docs/getting-started/index.md` as a decision guide. Developers who land on the wrong package first (e.g., Abstractions when they wanted the full stack) churn.

Surface the ladder in `README.md`:

```markdown
## Packages

| Package | Install If... |
|---------|--------------|
| `ModulusKit.Cli` | Starting a new project from scratch |
| `ModulusKit.Mediator` | Adding CQRS to an existing project |
| `ModulusKit.Mediator.Abstractions` | Building your own mediator implementation |
| `ModulusKit.Messaging` | Adding reliable messaging with outbox |
| `ModulusKit.Generators` | Eliminating handler registration boilerplate |
| `ModulusKit.Analyzers` | Enforcing modular boundaries at compile time |
```

See the **structuring-offer-ladders** skill for detailed package tier messaging decisions.

## Positioning Strategy

ModulusKit competes against:
1. **MediatR** — the incumbent; ModulusKit's differentiator is zero dependency + Result pattern + compile-time registration
2. **DIY patterns** — developers who write their own mediator; ModulusKit's differentiator is the CLI + opinionated structure
3. **Full frameworks (Wolverine, Brighter)** — ModulusKit is lighter and doesn't own the whole bus

The landing journey must communicate the right positioning for the entry point:

**For developers searching "replace MediatR":**
```markdown
<!-- README.md — add to comparisons section -->
## Why Not MediatR?
MediatR uses reflection for handler discovery at runtime.
ModulusKit.Generators discovers handlers at compile time — zero reflection, no ServiceLocator.
The Result pattern is built in, not bolted on.
```

**For developers searching "modular monolith starter":**
```markdown
<!-- docs/index.md feature card -->
title: Extraction Path
details: >
  Start as a modular monolith. Module boundaries are drawn by convention from day one.
  Extract to microservices by moving a module folder — no refactoring required.
```

**For developers searching "dotnet scaffolding cli":**
```markdown
<!-- NuGet search result — ModulusKit.Cli Description -->
dotnet tool: scaffold complete modular monolith solutions (CQRS, messaging, Aspire)
with one command. Every generated project follows Clean Architecture conventions.
```

## Anti-Patterns

### WARNING: Monetization CTAs That Break Trust

**The Problem:**
```markdown
<!-- BAD: "buy" signal on an OSS README destroys credibility -->
## Professional Support
For enterprise support, contact us at...

## ModulusKit Pro
Coming soon — advanced features for teams.
```

**Why This Breaks:**
Developers evaluating OSS libraries are looking for trust signals. A "Pro" tier announcement on a library with no established user base reads as vaporware. It creates fear of lock-in.

**The Fix:**
Add monetization signals only after the library has established adoption. A GitHub Sponsors link is lower-friction than a "Pro" mention. Keep the README focused on technical value until download count justifies it.

### WARNING: Positioning Against the Wrong Competitor

**The Problem:**
```markdown
<!-- BAD: comparing to Wolverine or Brighter without feature parity -->
ModulusKit is a better alternative to Wolverine.
```

**Why This Breaks:**
Wolverine and Brighter are mature, full-featured frameworks. Positioning ModulusKit as a replacement invites direct comparison on feature count — a comparison ModulusKit currently loses. It also confuses developers looking for a lightweight mediator.

**The Fix:**
Position against the problem ("No opinionated structure for modular monoliths") and against the pattern ("reflection-heavy handler discovery"), not against named competitors.

For ICP and value narrative decisions, see the **clarifying-market-fit** skill.
