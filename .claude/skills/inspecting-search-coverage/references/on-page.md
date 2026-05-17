# On-Page Reference — NuGet Listing & README Quality

## Contents
- NuGet listing anatomy
- README structure for package pages
- Per-package README vs. shared README tradeoffs
- Tag strategy

## NuGet Listing Anatomy

A developer landing on `nuget.org/packages/ModulusKit.Mediator` sees:
1. **Package ID** — `ModulusKit.Mediator`
2. **Description** — from `<Description>` (first ~150 chars shown in search results)
3. **README tab** — rendered Markdown from `<PackageReadmeFile>`
4. **Tags** — clickable links to other packages with same tag
5. **Project URL / Repository** — from `Directory.Build.props`

Descriptions above 250 chars are truncated in search results. Lead with the value, not the category.

## WARNING: Generic Descriptions Don't Rank

**The Problem:**

```xml
<!-- BAD — too generic, matches nothing specific -->
<Description>Abstractions for the Modulus mediator: IMediator, ICommand, IQuery, Result types, and pipeline behavior interfaces.</Description>
```

This lists what the package *contains*, not what it *solves*. A developer searching "dotnet cqrs no mediatR" won't find it.

**The Fix:**

```xml
<!-- GOOD — outcome-focused, keyword-rich -->
<Description>CQRS abstractions for .NET modular monoliths. Provides ICommand, IQuery, IStreamQuery, Result&lt;T&gt;, and IPipelineBehavior without a MediatR dependency. Drop-in interfaces for building handler-based architectures.</Description>
```

**Why:** NuGet full-text search indexes `<Description>`. Keywords like "modular monolith", "no MediatR", and "Result pattern" are what developers actually type.

## README Structure for Package Pages

Each package's README linked via `<PackageReadmeFile>` should follow this structure:

```markdown
# ModulusKit.Mediator

> Lightweight CQRS mediator for .NET — no MediatR, no reflection at runtime.

## Install

```
dotnet add package ModulusKit.Mediator
```

## Quick Start

```csharp
// 1. Register
services.AddModulusMediator();

// 2. Define a command
public record CreateUserCommand(string Email) : ICommand<UserId>;

// 3. Handle it
public sealed class CreateUserHandler(IUserRepository repo)
    : ICommandHandler<CreateUserCommand, UserId>
{
    public async Task<Result<UserId>> Handle(CreateUserCommand cmd, CancellationToken ct)
        => await repo.CreateAsync(cmd.Email, ct);
}

// 4. Send it
var result = await mediator.Send(new CreateUserCommand("user@example.com"));
```

## Why Not MediatR?

[Concise differentiation — see competitive.md]

## Pipeline Behaviors

[Show the behavior order]

## Result Pattern

[Show implicit conversions]
```

## Per-Package README vs. Shared README

| Approach | Use When |
|----------|----------|
| Per-package README (linked from each `.csproj`) | Package has distinct API worth showing in isolation |
| Shared root README (used by Cli today) | Package IS the product (CLI) or abstractions only |

Currently, `ModulusKit.Cli` correctly uses the root README. `ModulusKit.Mediator` has its own. `ModulusKit.Analyzers` has NO readme linked — this is the biggest gap.

## Tag Strategy

Tags are the primary filter on NuGet.org's browse experience. Use all relevant keywords:

```xml
<!-- ModulusKit.Messaging — current (too sparse) -->
<PackageTags>messaging;masstransit;outbox;inbox</PackageTags>

<!-- ModulusKit.Messaging — improved -->
<PackageTags>messaging;masstransit;outbox;inbox;transactional-outbox;integration-events;modular-monolith;rabbitmq;azure-service-bus;reliable-messaging</PackageTags>
```

NuGet.org indexes tags separately from description — a tag match ranks higher than a description match. Max 20 tags; prioritize specificity over quantity.

## Recommended Tags Per Package

| Package | Add These Tags |
|---------|---------------|
| `ModulusKit.Mediator` | `modular-monolith;dotnet;no-mediatR;handler;command;query` |
| `ModulusKit.Mediator.Abstractions` | `abstractions;contracts;modular-monolith;result-pattern` |
| `ModulusKit.Messaging` | `transactional-outbox;reliable-messaging;modular-monolith;rabbitmq;azure-service-bus` |
| `ModulusKit.Analyzers` | `modular-monolith;architecture;compile-time;conventions` |
| `ModulusKit.Generators` | `auto-registration;compile-time;modular-monolith;handler-registration` |
| `ModulusKit.Cli` | `scaffolding;dotnet-tool;modular-monolith;aspire;template` |

See the **crafting-page-messaging** skill for description copy tone and the **clarifying-market-fit** skill for positioning language.
