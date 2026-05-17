# Conversion Optimization Reference

## Contents
- Developer Conversion Funnel
- Friction Points by Surface
- Anti-Patterns
- Copy Patterns That Convert

---

## Developer Conversion Funnel

For ModulusKit, "conversion" is a developer running their first `modulus init` or adding their first package. The funnel:

```
Search ("dotnet modular monolith scaffold")
  → GitHub README or NuGet page
    → docs/index.md
      → Getting Started page
        → dotnet tool install -g ModulusKit.Cli
          → modulus init MySolution   ← CONVERSION
```

Every surface must reduce friction to the next step, not try to explain everything.

## Friction Points by Surface

### docs/index.md

**Friction**: Feature cards are equally weighted — no visual hierarchy guiding the eye to the install command.

```markdown
<!-- BAD: 6 feature cards, no CTA prominence -->
features:
  - title: CLI Scaffolding
  - title: CQRS Mediator
  - title: Messaging & Outbox
  - title: Clean Architecture
  - title: Microservice Extraction Path
  - title: Aspire Integration

<!-- GOOD: Lead with the action, features support -->
actions:
  - text: Scaffold Your First Solution
    link: /getting-started/
    theme: brand
  - text: View Packages on NuGet
    link: https://www.nuget.org/packages?q=ModulusKit
    theme: alt
# Keep features but reorder: CLI first (action), then Mediator, Messaging
```

### README.md

**Friction**: Architecture diagram appears before the install command. Developers who just want to try it have to scroll past a mermaid diagram.

```markdown
<!-- BAD: diagram before install -->
## Architecture
[mermaid diagram]

## Installation
dotnet tool install -g ModulusKit.Cli

<!-- GOOD: install within first 20 lines, diagram later -->
## Quick Start
dotnet tool install -g ModulusKit.Cli
modulus init MyApp --aspire

[one-liner result description]

## Architecture  ← move below the fold
```

### NuGet Package Pages

**Friction**: `<Description>` elements in `.csproj` files are truncated by NuGet to ~160 chars in search results. Front-load the differentiator.

```xml
<!-- src/Modulus.Generators/Modulus.Generators.csproj -->
<!-- BAD: generic, buries the value -->
<Description>Incremental source generators for Modulus, including StronglyTypedId.</Description>

<!-- GOOD: differentiator first, then detail -->
<Description>Source generators for ModulusKit: auto-registers all CQRS handlers at compile time (no reflection) and generates StronglyTypedId types with EF Core converters.</Description>
<PackageTags>source-generator;strongly-typed-id;roslyn;cqrs;compile-time;modular-monolith</PackageTags>
```

```xml
<!-- src/Modulus.Analyzers/Modulus.Analyzers.csproj -->
<!-- BAD: no specifics -->
<Description>Roslyn analyzers and code fixes for Modulus conventions.</Description>

<!-- GOOD: name the rules, signal the value -->
<Description>Roslyn analyzers (MOD001-MOD005) enforcing modular monolith boundaries: cross-module reference detection, Result pattern compliance, and infrastructure layer isolation.</Description>
<PackageTags>analyzer;roslyn;code-fix;modular-monolith;architecture;result-pattern</PackageTags>
```

## Anti-Patterns

### WARNING: Feature Lists Without Outcomes

**The Problem:**
```markdown
<!-- BAD — describes features, not outcomes -->
- Pipeline behaviors
- ValidationBehavior
- LoggingBehavior
- UnhandledExceptionBehavior
- MetricsBehavior
```

**Why This Breaks:**
Developers scan for "will this solve my problem," not "does this have a feature named X." A list of behavior class names forces the developer to do mental translation work.

**The Fix:**
```markdown
<!-- GOOD — outcomes first, feature name secondary -->
- Automatic validation on every command (ValidationBehavior)
- Structured logging with timing per request (LoggingBehavior)
- Exception → Result conversion so unhandled exceptions never leak (UnhandledExceptionBehavior)
```

### WARNING: Burying the Differentiator

**The Problem:**
```markdown
<!-- BAD: "no MediatR dependency" buried in paragraph 3 -->
ModulusKit.Mediator is a CQRS mediator for .NET. It supports pipeline behaviors,
FluentValidation integration, and the Result pattern. Unlike other mediators, it
does not depend on MediatR.
```

**Why This Breaks:**
The primary reason a developer chooses ModulusKit over MediatR is stated last, after they may have already scrolled away.

**The Fix:**
```markdown
<!-- GOOD: differentiator in the first sentence -->
A zero-dependency CQRS mediator for .NET — no MediatR, no reflection-heavy registration.
Pipeline behaviors, FluentValidation, and the Result pattern out of the box.
```

## Copy Patterns That Convert

| Pattern | Usage | Example |
|---------|-------|---------|
| **Command as title** | Docs quick-start sections | `modulus init MySolution --aspire` |
| **Before/After** | Showing elimination of boilerplate | "Before: 50 lines of DI setup. After: `services.AddModulusMediator()`" |
| **Single constraint removal** | Differentiator signal | "No MediatR. No reflection. No runtime handler discovery." |
| **Time-to-value claim** | Hero tagline | "Production-ready modular monolith in seconds" |
| **Escape hatch signal** | Extraction path section | "Start as a monolith. Extract to microservices when ready — module boundaries are already drawn." |
