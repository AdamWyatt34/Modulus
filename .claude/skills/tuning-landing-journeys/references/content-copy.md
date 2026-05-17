# Content Copy Reference

## Contents
- Surface-by-Surface Copy Rules
- Headline Formulas
- Anti-Patterns
- Editing Workflow

---

## Surface-by-Surface Copy Rules

### docs/index.md Hero

File: `docs/index.md`

The VitePress `hero` block is the highest-value copy in the project. Three lines, in priority order:

```yaml
hero:
  name: "ModulusKit"                          # Brand anchor — never change
  text: "Modular Monolith Toolkit for .NET"   # Category + platform — keep short
  tagline: "Scaffold production-ready modular monoliths in seconds"  # Outcome claim
```

Rules:
- `text` names the category. Developers searching "modular monolith .NET" should recognize it immediately.
- `tagline` must contain a time-to-value signal ("in seconds", "in minutes") or a constraint removal ("no boilerplate", "zero config").
- NEVER put feature names in `tagline`. Features go in the feature cards below.

### README.md Hero

File: `README.md`

The first sentence after the badges is read by GitHub search visitors who haven't clicked anything yet.

```markdown
<!-- GOOD: problem category → solution → differentiator in one sentence -->
A CLI tool and library suite for scaffolding .NET modular monolith solutions —
with a built-in CQRS mediator, messaging with transactional outbox, and optional Aspire integration.
```

Badge order matters. Download count badge (NuGet) before build status — social proof before technical trust signal.

```markdown
<!-- GOOD badge order -->
[![NuGet Downloads](https://img.shields.io/nuget/dt/ModulusKit.Cli)](...)
[![Build Status](https://github.com/...)](...)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](...)
```

### NuGet Package `<Description>`

Files: `src/*/Modulus.*.csproj`

NuGet truncates descriptions at ~160 characters in search. The differentiator must appear in the first 80 characters.

```xml
<!-- ModulusKit.Mediator — GOOD -->
<Description>Zero-dependency CQRS mediator for .NET. Pipeline behaviors, FluentValidation, Result pattern. No MediatR required.</Description>

<!-- ModulusKit.Messaging — GOOD -->
<Description>MassTransit integration for .NET modular monoliths. Transactional outbox/inbox, RabbitMQ, Azure Service Bus, and in-memory transports.</Description>

<!-- ModulusKit.Cli — GOOD -->
<Description>dotnet tool: scaffold complete modular monolith solutions with CQRS, messaging, and Aspire in one command.</Description>
```

### Per-Package README.md

Files: `src/*/README.md`

These ship inside the NuGet package and are shown on nuget.org package pages. Structure:

```markdown
# ModulusKit.[PackageName]

[One-sentence outcome statement.]

## Installation
dotnet add package ModulusKit.[PackageName]

## Setup
[Minimal working code block — must compile with zero additional setup]

## Key Features
- [Outcome], not just [feature name]

## See Also
- [Link to full docs page]
```

NEVER include architecture diagrams or long prose here. Developers reading NuGet READMEs want copy-paste code.

### CLI Help Text

Files: `src/Modulus.Cli/Commands/*.cs`

Every `Command` description and every `Argument`/`Option` description is surfaced in `modulus --help` output. Write them as micro-ads.

```csharp
// GOOD: sub-command descriptions as outcome statements
new Command("init", "Scaffold a complete modular monolith solution with CQRS, messaging, and optional Aspire")
new Command("add-module", "Add a bounded-context module (Domain, Application, Infrastructure, Api projects)")
new Command("add-entity", "Generate a domain entity with StronglyTypedId, EF Core config, and optional aggregate root")
new Command("add-command", "Generate a CQRS command, handler, and FluentValidation validator")
new Command("add-query", "Generate a CQRS query and handler returning Result<T>")
new Command("add-endpoint", "Wire a command or query to a Minimal API endpoint")
```

## Headline Formulas

| Formula | Example | Use When |
|---------|---------|----------|
| `[Outcome] in [Time]` | "Scaffold a module in 10 seconds" | Hero taglines |
| `[Constraint removed]` | "No MediatR. No reflection." | Differentiator callouts |
| `[Category] for [platform]` | "CQRS mediator for .NET modular monoliths" | Package titles |
| `[Action] → [Result]` | "Run `modulus init` → production-ready solution" | Quick-start intros |
| `[Start state] → [End state]` | "Monolith today. Microservices when ready." | Extraction path sections |

## Anti-Patterns

### WARNING: Passive Voice in Action Sections

**The Problem:**
```markdown
<!-- BAD -->
The solution can be scaffolded using the CLI tool.
```

**The Fix:**
```markdown
<!-- GOOD -->
Scaffold the solution with one command:
dotnet tool install -g ModulusKit.Cli && modulus init MyApp
```

### WARNING: Jargon-First Intros

**The Problem:**
```markdown
<!-- BAD — assumes the reader already knows what they need -->
ModulusKit.Mediator implements IPipelineBehavior<TRequest, TResponse> composition
with registration-order execution semantics.
```

**The Fix:**
```markdown
<!-- GOOD — problem first, jargon second -->
Every command and query flows through a configurable pipeline.
Add cross-cutting concerns (validation, logging, metrics) without touching handler code.
```

## Editing Workflow

1. Read the current copy in context (`Read docs/index.md` or the target `.csproj`)
2. Identify: What is the first sentence? Does it state the outcome or the feature?
3. Apply the Hierarchy: Problem → Solution → Differentiator → Action
4. Validate length: `<Description>` ≤ 160 chars, tagline ≤ 80 chars
5. Check: Does every code block compile standalone?

For positioning decisions before writing copy, see the **clarifying-market-fit** skill.
For copy in specific components (NuGet descriptions, CLI text), see the **crafting-page-messaging** skill.
