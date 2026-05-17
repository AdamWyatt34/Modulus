# Strategy & Monetization Reference

## Contents
- ModulusKit's Value Model
- Package Tier Messaging
- Version as a Monetization Signal
- README Positioning Ladder
- Anti-Patterns

---

## ModulusKit's Value Model

ModulusKit is MIT-licensed open source. There is no paid tier. "Monetization" in this context means:
- **Adoption** (teams using it) → leads to GitHub stars, NuGet downloads, word-of-mouth
- **Contribution** (PRs, issues) → signals active ecosystem, increases trust
- **Attribution** (public projects referencing ModulusKit) → organic discovery

The strategy is not revenue extraction — it is ecosystem credibility. Every messaging decision should
increase developer trust and lower the perceived risk of adopting an open-source dependency.

For package tier positioning, see the **structuring-offer-ladders** skill.

---

## Package Tier Messaging

ModulusKit ships 7 packages. The README and NuGet descriptions must communicate which packages a
developer needs for their use case without requiring them to read all 7 descriptions.

### Current Package Metadata

```xml
<!-- ModulusKit.Mediator.Abstractions -->
<!-- No Description defined — relies on Directory.Build.props defaults -->

<!-- ModulusKit.Mediator -->
<Description>Lightweight CQRS mediator for .NET with pipeline behaviors, validation, logging, and a built-in Result pattern.</Description>

<!-- ModulusKit.Cli -->
<Description>CLI tool for scaffolding .NET modular monolith solutions with built-in CQRS mediator, messaging, and Aspire support.</Description>
```

The Abstractions packages are missing explicit `<Description>` fields — they inherit nothing meaningful
from `Directory.Build.props`. Add descriptions that communicate the dependency relationship:

```xml
<!-- Recommended for Modulus.Mediator.Abstractions.csproj -->
<Description>Core interfaces for ModulusKit mediator: ICommand, IQuery, IStreamQuery, Result&lt;T&gt;, and Error types. Reference this from domain/application layers without taking a runtime dependency on the mediator.</Description>

<!-- Recommended for Modulus.Messaging.Abstractions.csproj -->
<Description>Core interfaces for ModulusKit messaging: IIntegrationEvent, IMessageBus, and outbox/inbox models. Reference from integration event definitions without coupling to MassTransit.</Description>
```

The key value prop for Abstractions packages is **layer isolation** — domain layer depends on interfaces,
not implementations. Name that explicitly.

---

## Version as a Monetization Signal

For open-source libraries, version cadence signals health. A library at v1.0.1 with no activity for
6 months looks abandoned. Version messaging in changelogs and release notes should:

1. Appear on GitHub releases (primary) and potentially NuGet release notes field
2. Lead with the developer-facing benefit, not the internal change
3. Reference the CLI command or API surface that changed

```xml
<!-- Directory.Build.props — central version for all 7 packages -->
<Version>1.0.1</Version>
```

All packages publish at the same version. This simplifies compatibility reasoning for users — they
never have to wonder if `ModulusKit.Mediator 1.0.1` works with `ModulusKit.Generators 1.0.0`.
The messaging should reinforce this: "All ModulusKit packages are versioned together."

---

## README Positioning Ladder

The README serves as the adoption ladder. Developers arrive at different levels of intent:

| Arrival Intent | What They Need | Section |
|---------------|----------------|---------|
| "Is this the right library?" | One-line pitch + differentiator | Hero |
| "How do I install it?" | Install command per use case | Quick Start |
| "What can it do?" | Feature list with code snippets | Features |
| "Which packages do I need?" | Package table with use cases | Packages |
| "How do I extend it?" | Advanced patterns | Docs link |

The README must serve all four levels without forcing each reader to scroll through all sections.
Use anchored headers so the package table and quick start are directly linkable from NuGet.

### Package Table Template

```markdown
| Package | Install | Use When |
|---------|---------|----------|
| `ModulusKit.Mediator` | `dotnet add package ModulusKit.Mediator` | Adding CQRS to an existing project |
| `ModulusKit.Mediator.Abstractions` | `dotnet add package ModulusKit.Mediator.Abstractions` | Domain/application layer without runtime dependency |
| `ModulusKit.Generators` | `dotnet add package ModulusKit.Generators` | Auto-registering handlers via source generator |
| `ModulusKit.Cli` | `dotnet tool install --global ModulusKit.Cli` | Scaffolding a new modular monolith from scratch |
```

"Use When" is more persuasive than "Description" — it frames the package as a solution to a specific problem.

---

## Anti-Patterns

### WARNING: Missing Abstractions Package Descriptions

**The Problem:**
```xml
<!-- Modulus.Mediator.Abstractions.csproj — no Description field -->
<PackageId>ModulusKit.Mediator.Abstractions</PackageId>
```

**Why This Breaks:**
1. NuGet displays a blank or default description — fails the 5-second test
2. Developers don't understand why there are two mediator packages
3. Domain-layer-only installs require the Abstractions package — if it's unclear, developers install the full package unnecessarily

**The Fix:** Add explicit descriptions that call out the layer isolation value prop.

### WARNING: Version Skew Between Packages

**The Problem:**
If individual `.csproj` files ever specify their own `<Version>`, packages can ship at different versions.

**Why This Breaks:**
- Developers must explicitly reconcile versions in their own `Directory.Packages.props`
- Version mismatch between `ModulusKit.Generators` and `ModulusKit.Mediator.Abstractions` causes compilation errors in source generator output

**The Fix:**
```xml
<!-- Directory.Build.props — single source of truth -->
<Version>1.0.1</Version>
```

NEVER add `<Version>` to individual `.csproj` files. See the **csharp** skill for package versioning rules.
