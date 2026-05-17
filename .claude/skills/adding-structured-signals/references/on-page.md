# On-Page Reference — Structured Signals

## Contents
- NuGet.org Listing Anatomy
- Per-Package Tag Strategy
- Description Formula
- README Structure for NuGet Pages
- Anti-Patterns

---

## NuGet.org Listing Anatomy

NuGet.org search considers these fields in descending weight:
1. `PackageId` — exact and prefix matches rank highest
2. `<Description>` — full-text indexed, first 160 chars appear in search results
3. `<PackageTags>` — semicolon-delimited, used for tag-filter search
4. `<Title>` — optional, overrides PackageId in display but not search

The first sentence of `<Description>` is the meta description equivalent. Make it count.

## Per-Package Tag Strategy

**Core tags shared across all 7 packages:**

```
modular-monolith;dotnet;net10;moduluskit;clean-architecture;open-source
```

**Package-specific additions:**

```xml
<!-- ModulusKit.Mediator.Abstractions -->
<PackageTags>modular-monolith;dotnet;net10;moduluskit;cqrs;mediator;result-pattern;icommand;iquery;abstractions</PackageTags>

<!-- ModulusKit.Mediator -->
<PackageTags>modular-monolith;dotnet;net10;moduluskit;cqrs;mediator;pipeline;behaviors;fluent-validation;dependency-injection</PackageTags>

<!-- ModulusKit.Messaging.Abstractions -->
<PackageTags>modular-monolith;dotnet;net10;moduluskit;messaging;integration-events;outbox;inbox;abstractions</PackageTags>

<!-- ModulusKit.Messaging -->
<PackageTags>modular-monolith;dotnet;net10;moduluskit;messaging;masstransit;outbox;inbox;rabbitmq;azure-service-bus;transactional-outbox</PackageTags>

<!-- ModulusKit.Generators -->
<PackageTags>modular-monolith;dotnet;net10;moduluskit;source-generator;roslyn;auto-registration;compile-time;incremental-generator</PackageTags>

<!-- ModulusKit.Analyzers -->
<PackageTags>modular-monolith;dotnet;net10;moduluskit;roslyn-analyzer;code-analysis;architecture;linting;diagnostics;code-fix</PackageTags>

<!-- ModulusKit.Cli -->
<PackageTags>modular-monolith;dotnet;net10;moduluskit;dotnet-tool;cli;scaffolding;code-generation;template</PackageTags>
```

**Rule:** Max ~10 tags. Beyond that, NuGet.org weight dilutes. Use the most searched terms first.

## Description Formula

Pattern: `[Action verb] [what] for [context]. [Differentiator]. [Integration note if applicable].`

```xml
<!-- Mediator.Abstractions -->
<Description>CQRS interfaces and Result pattern types for .NET modular monolith development. Defines ICommand, IQuery, IStreamQuery, IDomainEvent, and IPipelineBehavior contracts. Pair with ModulusKit.Mediator for the full implementation.</Description>

<!-- Mediator -->
<Description>Custom CQRS mediator for .NET 10 modular monoliths. Includes pipeline behaviors for validation (FluentValidation), logging, metrics, and unhandled exception handling. Result pattern with implicit conversions. No MediatR dependency.</Description>

<!-- Messaging -->
<Description>MassTransit abstraction with transactional outbox/inbox for .NET modular monoliths. Reliable integration event delivery across RabbitMQ, Azure Service Bus, and in-memory transports. Integrates with ModulusKit.Mediator pipeline.</Description>

<!-- Generators -->
<Description>Roslyn source generator for automatic handler registration in ModulusKit modular monoliths. Scans for ICommandHandler, IQueryHandler, IDomainEventHandler, and AbstractValidator implementations at compile time. Eliminates manual DI registration.</Description>

<!-- Analyzers -->
<Description>Roslyn analyzers enforcing modular monolith architecture rules (MOD001-MOD005). Detects cross-module violations, missing Result returns, thrown exceptions for expected errors, and infrastructure attributes in domain layer.</Description>

<!-- CLI -->
<Description>dotnet tool for scaffolding complete .NET 10 modular monolith solutions using ModulusKit conventions. Run `dotnet tool install -g ModulusKit.Cli` then `modulus init MySolution --aspire` to generate a production-ready project structure.</Description>
```

---

## README Structure for NuGet Pages

The NuGet.org package page renders `PackageReadmeFile`. Structure it for a developer landing in 5 seconds:

```markdown
# ModulusKit.Mediator

> Custom CQRS mediator for .NET 10 modular monoliths — no MediatR dependency.

## Install

\`\`\`
dotnet add package ModulusKit.Mediator
\`\`\`

## Quick Start

\`\`\`csharp
// Register
services.AddModulusMediator();

// Define a command
public record CreateUserCommand(string Email) : ICommand<UserId>;

// Send it
var result = await mediator.Send(new CreateUserCommand("user@example.com"));
\`\`\`

## Pipeline

Requests flow: UnhandledExceptionBehavior → LoggingBehavior → ValidationBehavior → Handler

## Links

- [Full documentation](https://github.com/AdamWyatt34/Modulus)
- [NuGet packages](https://www.nuget.org/packages?q=ModulusKit)
```

---

## WARNING: Missing `<PackageReadmeFile>`

Without it, NuGet.org shows a blank package page. The package appears unmaintained.

```xml
<!-- GOOD — always set this -->
<PackageReadmeFile>README.md</PackageReadmeFile>
```

```xml
<!-- And include the file in the pack -->
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="/" />
</ItemGroup>
```

## WARNING: Keyword Stuffing in Tags

NuGet.org does not reward tag volume — it weights relevance. Seventeen vague tags is worse than
eight precise ones. Never tag with generic terms like `library`, `tool`, `utility`, `helper`.
