# Distribution Reference

## Contents
- Distribution Channels for ModulusKit
- NuGet.org as Primary Channel
- GitHub as Evaluation Channel
- Docs Site as Activation Channel
- Release Checklist

---

## Distribution Channels for ModulusKit

ModulusKit distributes through three channels. Each maps to a funnel stage and requires specific content:

| Channel | Funnel Stage | Primary Action |
|---------|-------------|----------------|
| NuGet.org | Discovery → Trial | `dotnet tool install` / `dotnet add package` |
| GitHub | Evaluation | README read, issue search, star |
| Docs site (`docs/`) | Activation | Quick Start completion |

There is no paid channel. All growth is organic via NuGet search rankings, GitHub discoverability, and word-of-mouth in .NET communities.

---

## NuGet.org as Primary Channel

NuGet.org is where trial begins. Two things drive discoverability: `<PackageTags>` and `<Description>`.

**Tags must match how developers search**, not how the library is categorized internally:

```xml
<!-- src/Modulus.Cli/Modulus.Cli.csproj -->
<PackageTags>dotnet;cli;scaffolding;modular-monolith;cqrs;mediator;tool</PackageTags>

<!-- src/Modulus.Mediator/Modulus.Mediator.csproj -->
<PackageTags>dotnet;cqrs;mediator;pipeline;result-pattern;commands;queries</PackageTags>

<!-- src/Modulus.Messaging/Modulus.Messaging.csproj -->
<PackageTags>dotnet;masstransit;outbox;inbox;messaging;rabbitmq;azure-service-bus</PackageTags>
```

**Version metadata drives trust.** Developers check version history before adopting a library. CI tagging must be consistent:

```yaml
# .github/workflows/ — version tags enable NuGet download trend visibility
- name: Pack
  run: dotnet pack --configuration Release -p:PackageVersion=${{ github.ref_name }}
```

See the **inspecting-search-coverage** skill for full NuGet metadata audit patterns.

---

## GitHub as Evaluation Channel

GitHub is where developers evaluate before they trial. The repo's README is the evaluation surface. Key distribution signals on GitHub:

- **Stars**: Social proof for NuGet page (badge is visible at top of README)
- **Issues**: Signals active maintenance — open issues without responses block adoption
- **Topics**: Enable GitHub topic search (`dotnet`, `cqrs`, `modular-monolith`)

```markdown
<!-- README.md — topics must be set in repo Settings → About, not in README -->
<!-- Ensure these topics are set on the GitHub repo: -->
<!-- dotnet, cqrs, mediator, modular-monolith, scaffolding, aspire, masstransit -->
```

Issue response time is a conversion signal. A developer who searches for known errors before installing will abandon if they find unanswered issues describing their exact problem.

---

## Docs Site as Activation Channel

The docs site (`docs/`) serves developers who've installed and are trying to activate. Navigation must mirror the adoption funnel:

```
Getting Started    ← Activation path (most important)
├── Installation   ← Trial → Activation bridge
├── Quick Start    ← First scaffold walkthrough
└── Add a Module   ← Adoption entry point

Packages           ← Evaluation path
├── Mediator
├── Messaging
└── Generators
```

AVOID docs site navigation that leads with concepts (Architecture, Design Decisions) — those serve contributors, not adopting developers. Put Quick Start first.

---

## Release Checklist

Copy and track progress when cutting a new release:

```
- [ ] Bump version in Directory.Build.props
- [ ] Update CHANGELOG.md with what changed
- [ ] Verify all <Description> fields are current
- [ ] Verify <PackageTags> include transport-specific tags if messaging changed
- [ ] Tag commit: git tag v{version}
- [ ] Push tag to trigger CI pack + publish
- [ ] Verify NuGet.org listing shows new version within 15 min
- [ ] Update README badge version if pinned
- [ ] Post release note to GitHub Releases with install command
```

### WARNING: Publishing Without Version Tag

**The Problem:** Pushing without a version tag results in a development-versioned package (e.g., `1.0.0-dev`) being published, or no publish at all if CI gates on tag presence.

**The Fix:** CI workflow must gate publish on `refs/tags/v*` pattern:

```yaml
# .github/workflows/publish.yml
on:
  push:
    tags:
      - 'v*'
```

NuGet packages cannot be deleted after publish — a malformed version is permanent and fragments install base.
