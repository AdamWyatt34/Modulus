# Strategy and Monetization Reference

## Contents
- ModulusKit Monetization Model
- Package Tier Strategy
- Conversion from Free to Paid (Future State)
- NuGet Sponsorship and Attribution
- Anti-Patterns

---

## ModulusKit Monetization Model

ModulusKit is currently **fully open source with MIT license** — all 7 NuGet packages are free. The current monetization model is indirect: the library demonstrates technical skill, attracts contributors, and builds reputation in the .NET ecosystem.

There is no paywall, no premium tier, and no subscription. All conversion events in the funnel lead to free adoption.

See the **structuring-offer-ladders** skill for analysis of how the package tiers create a value ladder, even without paid tiers.

---

## Package Tier Strategy

The 7 packages form a natural adoption ladder. Each tier unlocks more capability:

```
Tier 1 — Abstractions only (zero-dependency evaluation)
  ModulusKit.Mediator.Abstractions   ← Interfaces + Result pattern
  ModulusKit.Messaging.Abstractions  ← IIntegrationEvent, IMessageBus

Tier 2 — Full implementation (primary adoption target)
  ModulusKit.Mediator                ← Pipeline + behaviors + DI
  ModulusKit.Messaging               ← MassTransit + outbox processor

Tier 3 — Developer experience layer (stickiness)
  ModulusKit.Generators              ← Source-generated handler registration
  ModulusKit.Analyzers               ← Compile-time rule enforcement

Tier 4 — Scaffolding (acquisition and distribution)
  ModulusKit.Cli                     ← dotnet tool that generates Tier 1-3 usage
```

This ladder matters for conversion: a developer who starts at Tier 4 (CLI) is immediately exposed to Tier 1-3. A developer who starts at Tier 2 (NuGet) may never discover the CLI.

```csharp
// The generated Program.cs after modulus init surfaces the full stack
// This is the package ladder made visible to every adopting developer
builder.Services.AddModulusMediator();     // Tier 2
builder.Services.AddModulusHandlers();     // Tier 3 (source-generated)
builder.Services.AddModulusMessaging(o => // Tier 2
{
    o.Transport = Transport.InMemory;
});
```

---

## Conversion from Free to Paid (Future State)

If ModulusKit adds a paid tier, the highest-value features for gating are:

1. **Enterprise transport support** — Azure Service Bus configuration, multi-region outbox
2. **Analyzer rule customization** — custom MOD rule sets, `.editorconfig` presets
3. **CLI project templates** — domain-specific scaffold templates (e-commerce, SaaS, etc.)
4. **Priority issue support** — SLA-backed GitHub issue response

The free tier must remain fully functional for the adoption funnel to work. Gating core functionality (mediator, outbox, CLI) behind payment destroys the passive distribution loop.

**Rule:** Free = individually capable. Paid = organizationally capable.

---

## NuGet Sponsorship and Attribution

NuGet.org supports `<RepositoryUrl>` and `<PackageProjectUrl>` as attribution links. Both should point to the GitHub repo:

```xml
<!-- Directory.Build.props — applies to all packages -->
<PropertyGroup>
  <RepositoryUrl>https://github.com/AdamWyatt34/Modulus</RepositoryUrl>
  <PackageProjectUrl>https://adamwyatt34.github.io/Modulus/</PackageProjectUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```

`<PackageReadmeFile>` displays the README directly on NuGet.org. This is the highest-traffic free copy surface — more developers see it than visit GitHub.

---

## Adoption Retention Strategy

A developer library retains users through **upgrade friction** — the cost of switching away. ModulusKit creates retention through:

1. **Source generator coupling** — `AddModulusHandlers()` is generated and referenced in `Program.cs`
2. **Result pattern pervasiveness** — once handlers return `Result<T>`, removing it touches every handler
3. **Analyzer rules** — MOD001-005 become team standards; removing them requires policy change
4. **CLI-generated structure** — the directory layout and module conventions are locked in at scaffold time

This is retention by design, not lock-in. The patterns are good patterns — developers don't want to remove them.

---

## Anti-Patterns

### WARNING: Putting Breaking Changes in Minor Versions

**The Problem:** A team that has 15 modules using `ModulusKit.Mediator` and sees `1.1.0` break their `ICommandHandler<>` interface will not upgrade. They will fork or abandon.

**Why This Breaks:** NuGet versioning creates trust signals. Minor = additive, patch = fix, major = breaking. Violating semver destroys the trust that makes passive distribution work.

**The Fix:** Breaking changes ONLY in major versions. `Directory.Build.props` version bump must be deliberate:

```xml
<!-- Directory.Build.props — major bump = breaking, minor = additive, patch = fix -->
<PackageVersion>2.0.0</PackageVersion>  <!-- breaking: bumped from 1.x -->
```

### WARNING: MIT License Without NOTICE File for Contributors

If contributors add significant code, they retain copyright. NOTICE files prevent attribution ambiguity for enterprise adopters who require license audits. Missing NOTICE files are a blocker for some enterprise adoptions — enterprise teams cannot adopt libraries with unclear attribution.
