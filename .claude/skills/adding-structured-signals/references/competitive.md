# Competitive Reference — Structured Signals

## Contents
- Positioning Against MediatR
- NuGet Search Landscape
- Tag Differentiation Strategy
- Description Differentiation Examples
- Anti-Patterns

---

## Positioning Against MediatR

MediatR is the dominant search term in the CQRS/mediator space for .NET. ModulusKit must either
rank alongside it or explicitly differentiate for searches like "MediatR alternative" or
"MediatR without dependency".

**Target search intents:**
- "dotnet cqrs mediator no mediatR"
- "modular monolith scaffolding dotnet"
- "dotnet result pattern mediator"
- "transactional outbox dotnet"
- "roslyn source generator handler registration"

**Differentiators to call out explicitly in descriptions:**
1. No MediatR dependency — self-contained mediator
2. Result pattern built-in (MediatR returns raw objects)
3. Source generator auto-registers handlers (MediatR requires manual DI)
4. Full ecosystem: mediator + messaging + CLI + analyzers in one namespace
5. .NET 10 native (MediatR supports older runtimes)

## NuGet Search Landscape

Key competitors and what their metadata does well vs. ModulusKit:

| Package | Tag Strengths | Description Strength | Gap to Exploit |
|---------|--------------|---------------------|---------------|
| `MediatR` | `mediator`, `cqrs` | Very brief | No result pattern, no outbox |
| `Wolverine` | `messaging`, `outbox` | Detailed | No CQRS mediator built-in |
| `Ardalis.Result` | `result-pattern` | Focused | No mediator or messaging |
| `Clean.Architecture.Solution.Template` | `template`, `clean-architecture` | Template-focused | CLI-only, no runtime libs |

ModulusKit's unique position: the **only** NuGet family that covers mediator + result pattern +
messaging + outbox + source generators + analyzers + CLI as a coordinated set.

## Tag Differentiation Strategy

Use tags that competitors miss but developers search for:

```xml
<!-- Tags that surface ModulusKit for "MediatR alternative" searches -->
<PackageTags>
  cqrs;mediator;modular-monolith;result-pattern;pipeline;
  no-mediatR;zero-dependency-mediator;outbox-pattern;
  transactional-outbox;roslyn-source-generator;dotnet-tool;
  moduluskit;net10;clean-architecture
</PackageTags>
```

**Note:** `no-mediatR` is a direct competitor tag — use it on `ModulusKit.Mediator` only, not on
messaging/analyzer packages where MediatR isn't the comparison point.

## Description Differentiation Examples

### ModulusKit.Mediator vs. MediatR

```xml
<!-- What MediatR's description lacks: result pattern, source generators -->
<!-- ModulusKit.Mediator description — call out the gaps -->
<Description>
  Custom CQRS mediator for .NET 10 modular monoliths. Built-in Result pattern with implicit
  conversions eliminates nulls and exceptions for expected errors. Pipeline behaviors for
  validation (FluentValidation), logging, metrics, and exception handling. Handlers
  auto-registered via Roslyn source generator — no manual DI wiring. No MediatR dependency.
</Description>
```

### ModulusKit.Messaging vs. Wolverine

```xml
<!-- Wolverine's strength: messaging. ModulusKit's angle: Mediator integration -->
<Description>
  MassTransit abstraction with transactional outbox/inbox for .NET 10 modular monoliths.
  Integration events stored in outbox table within same EF Core transaction as domain data —
  guaranteed delivery even if the broker is unavailable. Supports RabbitMQ, Azure Service Bus,
  and in-memory transports. First-class integration with ModulusKit.Mediator pipeline.
</Description>
```

## README Comparison Table

Include a feature comparison in the README to intercept "vs MediatR" searches. Google surfaces
comparison tables in featured snippets.

```markdown
## How ModulusKit Compares

| Feature | ModulusKit | MediatR | Wolverine |
|---------|-----------|---------|-----------|
| CQRS mediator | ✅ Custom, no deps | ✅ Industry standard | ✅ Via Wolverine |
| Result pattern | ✅ Built-in | ❌ Return raw objects | ❌ |
| Handler auto-registration | ✅ Source generator | ❌ Manual DI | ✅ |
| Transactional outbox | ✅ Built-in | ❌ | ✅ |
| Architecture analyzers | ✅ MOD001-MOD005 | ❌ | ❌ |
| Scaffolding CLI | ✅ `modulus init` | ❌ | ❌ |
| .NET 10 native | ✅ | ✅ | ✅ |
```

---

## WARNING: Avoiding "No MediatR" Framing in Tag-Only Form

NEVER make "no-MediatR" the primary value proposition. It's a feature, not a benefit. The
description must lead with what ModulusKit **does**, not what it avoids. The no-dependency angle
is a secondary trust signal for developers already evaluating the mediator pattern.

```xml
<!-- BAD — leads with the negative -->
<Description>A .NET mediator that doesn't use MediatR. Provides CQRS patterns.</Description>

<!-- GOOD — leads with the value, no-dependency is supporting detail -->
<Description>Custom CQRS mediator for .NET 10 modular monoliths with built-in Result pattern and source-generated handler registration. No MediatR dependency required.</Description>
```

## WARNING: Copying Competitor Tags Verbatim

Using exactly the same tags as MediatR (e.g., just `mediator;cqrs`) puts ModulusKit in direct
rank competition with an established package that has millions of downloads. Instead, use a
superset that includes unique terms ModulusKit owns (e.g., `modular-monolith`, `result-pattern`,
`outbox-pattern`, `moduluskit`).
