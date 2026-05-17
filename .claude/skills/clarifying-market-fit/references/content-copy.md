# Content Copy Reference

## Contents
- Copy voice and tone for developer audiences
- README section copy patterns
- Docs feature card copy
- CLI help text copy
- NuGet description copy
- Anti-patterns

---

## Voice and Tone

ModulusKit addresses senior .NET developers — people who have already felt the pain of manual
CQRS wiring, MediatR version conflicts, and outbox roll-your-own. Write as a peer, not a
salesperson. Assume technical competence; explain tradeoffs, not basics.

**Tone principles:**
- Direct: "No MediatR dependency" not "offers an alternative to MediatR"
- Outcome-led: "Run in production in 60s" not "makes development easier"
- Honest about scope: "modular monolith" not "microservices"

## README Section Copy Patterns

### Badge Line
```markdown
<!-- GOOD — version + license are the only badges a developer cares about -->
[![NuGet](https://img.shields.io/nuget/v/ModulusKit.Cli.svg)](https://www.nuget.org/packages/ModulusKit.Cli)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

<!-- BAD — build status, coverage badges without CI context cause confusion -->
[![Build](https://img.shields.io/github/actions/workflow/status/...)]
[![Coverage](https://codecov.io/gh/...)]
```

### "What is this?" Paragraph
```markdown
<!-- GOOD — one paragraph, three sentences max, names the target audience explicitly -->
Modulus helps you build **modular monoliths** in .NET. Instead of starting with microservices,
you start with a single deployable unit where each feature lives in its own module with clear
boundaries. When the time comes, any module can be extracted into a standalone service.

<!-- BAD — lists libraries first, audience second -->
ModulusKit is a suite of NuGet packages including a mediator, messaging abstractions,
source generators, and a CLI tool for .NET developers working on modular architectures.
```

### Feature List in README
Each feature bullet must follow: **title → one-line outcome**.

```markdown
<!-- GOOD -->
- **CLI Scaffolding** — `modulus init` creates a complete solution with all layers wired
- **CQRS Mediator** — Pipeline behaviors, Result pattern, FluentValidation. No MediatR.
- **Transactional Outbox** — Integration events survive broker downtime without extra code

<!-- BAD — describes what it is, not what you get -->
- **CLI** — A command-line tool built with System.CommandLine
- **Mediator** — Implements the mediator pattern
- **Outbox** — Stores messages in a database table
```

## Docs Feature Card Copy (`docs/index.md`)

Feature cards in VitePress hero must fit in 2 lines of `details` text. Lead with benefit:

```yaml
# GOOD
features:
  - icon: ">_"
    title: CLI Scaffolding
    details: Scaffold solutions, modules, entities, and endpoints with one command. Aspire-ready out of the box.

# BAD — describes the tool, not the benefit
  - icon: ">_"
    title: CLI Tool
    details: A command-line interface built on System.CommandLine that supports multiple subcommands.
```

## CLI Help Text Copy

CLI `--help` output is the inline docs for power users. Keep descriptions to one line, job-to-be-done focused:

```csharp
// GOOD — describes the job, names the output
var command = new Command("init", "Scaffold a new modular monolith solution with CQRS, messaging, and optional Aspire.");

// BAD — describes the action abstractly
var command = new Command("init", "Initialize a new solution.");
```

Option descriptions should name the default and effect:

```csharp
// GOOD
var aspireOption = new Option<bool>("--aspire", "Add .NET Aspire orchestration project (default: false)");
var transportOption = new Option<string>("--transport", "Message transport: inmemory | rabbitmq | azureservicebus (default: inmemory)");

// BAD — no default, no options listed
var transportOption = new Option<string>("--transport", "The transport to use");
```

## NuGet Description Copy

NuGet descriptions must be ≤160 characters for full display in search. Formula:
`[What it does] for [who]. [Key differentiator].`

```xml
<!-- Mediator package -->
<Description>Lightweight CQRS mediator for .NET with pipeline behaviors, Result pattern, and FluentValidation. No MediatR dependency.</Description>

<!-- Messaging package -->
<Description>MassTransit integration for .NET modular monoliths. Transactional outbox, InMemory/RabbitMQ/Azure Service Bus transports.</Description>

<!-- CLI package -->
<Description>dotnet tool to scaffold production-ready modular monolith solutions. CQRS, outbox, and Aspire wired in one command.</Description>
```

## WARNING: Feature-First Copy

**The Problem:** Writing for what the library *is* rather than what the developer *gets*.

```markdown
<!-- BAD — internal description -->
Modulus.Mediator implements a generic mediator pattern using reflection-based handler
discovery registered through Microsoft.Extensions.DependencyInjection.
```

**Why This Breaks:** Developers already know what a mediator is. They need to know why yours
is worth switching to. Features without outcomes are noise.

**The Fix:**
```markdown
<!-- GOOD — developer outcome first -->
Send commands and queries with one line. Pipeline behaviors handle logging, validation,
and error handling automatically — no boilerplate in your handlers.
```
