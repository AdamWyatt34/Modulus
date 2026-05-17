# Content Copy Reference

## Contents
- Package-Level Value Props
- CLI Help Text Standards
- README Section Hierarchy
- Anti-Patterns in Library Copy
- Copy Templates

Developer-facing copy for an open-source library ecosystem has one job: reduce the time from "I found this package" to "I shipped code using it." Every word of copy is competing with the developer's alternative of writing it themselves.

---

## Package-Level Value Props

Each package needs a two-layer value statement: one sentence for NuGet search, one paragraph for the package README or NuGet detail page.

### ModulusKit.Mediator.Abstractions

```
NuGet (≤200 chars):
"ICommand, IQuery, IStreamQuery, Result<T>, and Error types for CQRS.
Zero runtime dependencies — safe for any library to reference."

Detail page opening:
Provides the type contracts for ModulusKit's CQRS mediator. Reference this package
from library projects to accept commands and queries without depending on any
specific mediator implementation.
```

### ModulusKit.Mediator

```
NuGet (≤200 chars):
"In-process CQRS mediator with pipeline behaviors, Result<T> pattern,
FluentValidation integration, and zero MediatR dependency."

Detail page opening:
Wire up validated, logged, and instrumented command/query handling in 3 lines.
The mediator pipeline runs UnhandledExceptionBehavior → LoggingBehavior →
ValidationBehavior → Handler on every request.
```

### ModulusKit.Generators

```
NuGet (≤200 chars):
"Roslyn source generator that auto-registers all ModulusKit handlers at
compile time. Replaces manual ICommandHandler DI wiring."
```

### ModulusKit.Cli

```
NuGet (≤200 chars):
"dotnet tool that scaffolds a complete modular monolith solution with
CQRS, RabbitMQ/ASB messaging, and optional Aspire integration."
```

---

## CLI Help Text Standards

The `modulus` CLI is the highest-value tier. Its `--help` output is the first interaction most developers have with the full ecosystem.

```
GOOD — concrete, action-oriented:
  modulus init <SolutionName> [--aspire] [--transport <rabbitmq|azureservicebus|inmemory>]

  Scaffolds a complete modular monolith solution including:
    - Solution structure with src/ and tests/ directories
    - Modulus.Mediator + Modulus.Messaging wired in Program.cs
    - Sample module with command, handler, validator, and integration event
    - Aspire AppHost project (if --aspire specified)

BAD — vague, no payoff:
  Creates a new ModulusKit solution.
```

```powershell
# Validate CLI help copy stays accurate after changes
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- --help
dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- init --help
```

---

## README Section Hierarchy

The README is the primary conversion surface. Section order must follow the developer's decision flow:

```markdown
# ModulusKit                          ← What it is (1 sentence)
> Tagline: concrete outcome           ← Why it matters (1 line)

## Packages                           ← Which one do I install?
## Quick Start                        ← Does it work in 5 minutes?
## Concepts                           ← How does it fit together?
## Architecture                       ← Can I trust this at scale?
## Contributing                       ← Can I fix this if it's wrong?
```

NEVER lead with architecture. Developers need to see working code before they invest in understanding design.

---

## WARNING: Feature Lists Without Outcomes

**The Problem:**
```markdown
<!-- BAD — feature list with no developer outcome -->
## Features
- CQRS mediator
- Pipeline behaviors
- Result pattern
- Source generators
- Roslyn analyzers
- CLI scaffolding
- MassTransit integration
```

**Why This Breaks:**
1. Lists don't answer "why should I switch from MediatR?"
2. No indication of what problems these features solve
3. Every competing library has a similar feature list

**The Fix:**
```markdown
## What You Get

| Problem | ModulusKit Solution |
|---------|---------------------|
| MediatR license concerns | Custom mediator, MIT licensed |
| Manual handler DI registration | `AddModulusHandlers()` — source-generated at compile time |
| Silent exception swallowing | `Result<T>` pattern enforced by MOD002 analyzer |
| Cross-module coupling | MOD001 blocks direct cross-module references at compile time |
| New project setup takes hours | `modulus init` scaffolds everything in 30 seconds |
```

---

## Copy Templates

### Per-Package README opener

```markdown
# ModulusKit.[PackageName]

[One concrete sentence: what this package provides and who should install it]

```powershell
dotnet add package ModulusKit.[PackageName]
```

## Prerequisites
- [Package dependency 1] ([link to that package])
- .NET 10.0+
```

### Migration section template

```markdown
## Migrating from [Version X] to [Version Y]

### Breaking changes
- `[OldAPI]` → `[NewAPI]`: [One-line reason]

### Non-breaking additions
- [Feature]: [One-line description]

### Steps
1. Update `Directory.Packages.props`: `ModulusKit.* → [new version]`
2. [Specific action if API changed]
3. Run `dotnet build` — any MOD* analyzer warnings indicate required changes
```
