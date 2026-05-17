# Distribution Reference

## Contents
- Primary distribution channels for ModulusKit
- NuGet discoverability
- GitHub repository optimization
- Docs site SEO
- CLI tool discoverability
- Anti-patterns

---

## Primary Distribution Channels

ModulusKit distributes through three channels, each with distinct discovery paths:

| Channel | Surface | Discovery Mechanism |
|---------|---------|---------------------|
| NuGet.org | Package search | `<PackageTags>`, `<Description>` |
| GitHub | Repository | README, topics, stars |
| docs site | VitePress | Google search, docs links |

## NuGet Discoverability

NuGet search ranks on: package ID, tags, download count, and recency.

**Tag strategy** — every package must include `modular-monolith` so all seven packages
cross-link in search results when a developer searches that term:

```xml
<!-- ModulusKit.Mediator -->
<PackageTags>mediator;cqrs;result-pattern;pipeline;modular-monolith;dotnet</PackageTags>

<!-- ModulusKit.Messaging -->
<PackageTags>messaging;masstransit;rabbitmq;azure-service-bus;outbox;modular-monolith;dotnet</PackageTags>

<!-- ModulusKit.Cli -->
<PackageTags>dotnet-tool;scaffolding;modular-monolith;cqrs;aspire;codegen</PackageTags>

<!-- ModulusKit.Generators -->
<PackageTags>source-generator;strongly-typed-id;roslyn;modular-monolith;dotnet</PackageTags>
```

**Package ID matters more than description for search ranking.** `ModulusKit.*` is the right
prefix — it's unique, searchable, and groups all packages together in "other packages by author"
on NuGet.

## GitHub Repository Optimization

GitHub search and "Explore" surfaces projects by: topics, README content, and star velocity.

Topics to add in the GitHub repository settings UI:
```
dotnet  csharp  modular-monolith  cqrs  mediator  aspire  scaffolding  cli  outbox  result-pattern
```

The `README.md` first paragraph should include the exact phrases developers search:
- "modular monolith"
- "CQRS mediator"
- "transactional outbox"
- ".NET"

These are verbatim search terms. If they don't appear in the first paragraph, GitHub search
ranks the project lower.

## Docs Site SEO (`docs/`)

The VitePress docs site at `docs/` generates static HTML. Each page's `<title>` and first
`<h1>` are the primary SEO signal.

```markdown
<!-- docs/getting-started/index.md -->
<!-- GOOD — include the target keyword in the H1 -->
# Getting Started with ModulusKit — Scaffold a .NET Modular Monolith

<!-- BAD — generic title -->
# Getting Started
```

VitePress uses the first frontmatter `title` or the first `#` heading as the page title.
Ensure every doc page has a descriptive, keyword-rich heading.

```yaml
# docs/mediator/index.md
---
title: CQRS Mediator — ModulusKit
description: Lightweight CQRS mediator for .NET with pipeline behaviors and Result pattern. No MediatR dependency.
---
```

## CLI Tool Discoverability

`ModulusKit.Cli` is a `dotnet tool`. Discoverability paths:
- `dotnet tool search modulus` — matches on package ID and tags
- NuGet.org tool gallery — ranks on description quality

The `ToolCommandName` must be short and memorable:
```xml
<!-- GOOD — one word, easy to remember -->
<ToolCommandName>modulus</ToolCommandName>

<!-- BAD — namespace-like, hard to type -->
<ToolCommandName>moduluskit-cli</ToolCommandName>
```

## WARNING: Inconsistent Package Naming

**The Problem:**
```xml
<!-- Some packages missing the ModulusKit prefix -->
<PackageId>Modulus.Cli</PackageId>
<PackageId>ModulusKit.Mediator</PackageId>
```

**Why This Breaks:** NuGet's "Other packages by this author" groups by publisher, but the
package list looks inconsistent and confuses developers trying to find related packages.
The `ModulusKit.*` prefix is what groups them visually in search results.

**The Fix:** All seven packages must use `ModulusKit.*` — never `Modulus.*`.

## Distribution Checklist

Copy and track progress:
- [ ] All 7 packages have `modular-monolith` in `<PackageTags>`
- [ ] GitHub repository topics include: `dotnet`, `modular-monolith`, `cqrs`, `scaffolding`
- [ ] `ToolCommandName` is `modulus`
- [ ] Every doc page has a keyword-rich `<h1>` or frontmatter `title`
- [ ] README first paragraph contains: "modular monolith", "CQRS", ".NET"
- [ ] `PackageProjectUrl` points to the docs site (not GitHub)
