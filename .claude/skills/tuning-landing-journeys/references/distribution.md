# Distribution Reference

## Contents
- Distribution Channels
- Channel-Specific Optimization
- Cross-Channel Consistency Rules
- Anti-Patterns

---

## Distribution Channels

ModulusKit distributes through four channels. Each has different first-impression mechanics.

| Channel | Entry Point | Developer Intent | Key Metric |
|---------|------------|-----------------|-----------|
| **NuGet.org** | Search results page | "Does this package solve my problem?" | Install count / search rank |
| **GitHub** | Repository root | "Is this maintained? Can I trust it?" | Stars / last commit date |
| **VitePress docs** | `docs/index.md` | "How do I get started?" | Time-to-first-scaffold |
| **CLI `--help`** | `modulus --help` terminal output | "What can I do next?" | Sub-command adoption rate |

## Channel-Specific Optimization

### NuGet.org

NuGet search ranks packages by download count, relevance to query terms, and recency. `<PackageTags>` directly feeds the relevance signal.

**Tag strategy**: Include both the problem-space terms developers search AND the solution-space terms:

```xml
<!-- ModulusKit.Mediator — covers both search intents -->
<PackageTags>mediator;cqrs;pipeline;result-pattern;modular-monolith;no-mediatR;dotnet</PackageTags>

<!-- ModulusKit.Messaging — transport options are searchable -->
<PackageTags>messaging;masstransit;rabbitmq;azure-service-bus;outbox;inbox;integration-events;modular-monolith</PackageTags>

<!-- ModulusKit.Cli — tool-specific tags -->
<PackageTags>dotnet-tool;scaffolding;modular-monolith;cqrs;aspire;code-generator;cli</PackageTags>
```

**Repository URL**: Set in `Directory.Build.props` — NuGet displays it as a trust signal:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <RepositoryUrl>https://github.com/AdamWyatt34/Modulus</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageProjectUrl>https://moduluskit.dev</PackageProjectUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```

### GitHub Repository

The GitHub repository page is the trust-evaluation surface. Developers check: stars, last commit, open issues, README quality.

Signal hierarchy in `README.md`:
1. **Badges** (build passing, NuGet version, downloads) — immediately visible
2. **One-sentence description** — what it is
3. **Quick start** — install command within first scroll
4. **Feature list** — outcome-focused bullets
5. **Architecture diagram** — for developers who want depth

```markdown
<!-- README.md — GOOD badge block (version + downloads + build + license) -->
[![NuGet Version](https://img.shields.io/nuget/v/ModulusKit.Cli?label=ModulusKit.Cli)](https://www.nuget.org/packages/ModulusKit.Cli)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ModulusKit.Cli)](https://www.nuget.org/packages/ModulusKit.Cli)
[![Build](https://github.com/AdamWyatt34/Modulus/actions/workflows/ci.yml/badge.svg)](https://github.com/AdamWyatt34/Modulus/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
```

### VitePress Docs (`docs/index.md`)

The docs landing page serves developers who already chose to evaluate ModulusKit. They want to reach the first `modulus init` command fast.

Optimize the action button hierarchy:
```yaml
# docs/index.md
hero:
  actions:
    - text: Get Started          # Primary — goes to /getting-started/
      link: /getting-started/
      theme: brand
    - text: Browse Packages      # Secondary — NuGet for "I want the library only"
      link: https://www.nuget.org/packages?q=ModulusKit
      theme: alt
```

Feature cards should link to the most actionable docs sub-page, not just the feature index:

```yaml
features:
  - title: CLI Scaffolding
    link: /cli/init          # ← specific command, not /cli/
  - title: CQRS Mediator
    link: /mediator/commands-queries   # ← usage page, not /mediator/
```

### CLI `--help` Output

`modulus --help` is shown every time a developer forgets a command name. It's a navigation surface. Sub-command descriptions are the only navigation aid available.

```csharp
// src/Modulus.Cli/Commands/AddModuleCommand.cs
// The description must answer: "After init, what do I do next?"
new Command("add-module",
    "Add a bounded-context module (Domain + Application + Infrastructure + Api projects)")
```

The `--help` output for each sub-command also surfaces option descriptions. These are the CLI equivalent of form field labels:

```csharp
// src/Modulus.Cli/Commands/AddEntityCommand.cs
// GOOD: option descriptions explain the outcome, not just the parameter name
var aggregateOption = new Option<bool>("--aggregate",
    "Generate as AggregateRoot with domain event support instead of plain Entity");

var idTypeOption = new Option<string>("--id-type",
    "ID backing type: guid (default), int, long, string, or a custom StronglyTypedId name");
```

## Cross-Channel Consistency Rules

1. **Package name**: Always `ModulusKit.*`, never `Modulus.*` on public-facing surfaces
2. **Differentiator phrase**: "no MediatR dependency" must appear on both README and the Mediator package description
3. **Install command format**: Always `dotnet add package ModulusKit.[Name]` — consistent across all READMEs
4. **CLI tool install**: Always `dotnet tool install -g ModulusKit.Cli` — consistent across README and docs

## Anti-Patterns

### WARNING: Tag Stuffing Without Relevance

**The Problem:**
```xml
<!-- BAD: irrelevant tags dilute relevance signal -->
<PackageTags>csharp;dotnet;library;nuget;package;open-source;modulus</PackageTags>
```

`csharp`, `dotnet`, `library`, `nuget`, `package` are too broad — every package has them, they add no search signal. `open-source` and `modulus` are not search terms developers use.

**The Fix:** Use only terms developers type when looking for this specific capability.

### WARNING: Inconsistent Package Naming in Copy

**The Problem:**
```markdown
<!-- BAD: mixing Modulus.* and ModulusKit.* in same README -->
Install `Modulus.Mediator` to get started.
...
dotnet add package ModulusKit.Mediator
```

**Why This Breaks:** Developers copy the wrong name into their terminal, get a 404 from NuGet, and lose trust. One bad experience kills adoption.

**The Fix:** Global search-replace across all `.md` files before any release. The canonical name is `ModulusKit.*`.
