# Strategy & Monetization Reference

## Contents
- OSS Monetization Model for ModulusKit
- Package Tier Strategy Rationale
- Positioning Against MediatR
- Versioning as a Strategic Lever
- Anti-Patterns in OSS Monetization

ModulusKit is MIT-licensed open-source with no direct monetization. The strategy is ecosystem adoption: broad usage of the free packages creates demand for consulting, commercial support, and future paid tooling. Every architectural decision in the package ladder must reinforce adoption breadth first, depth second.

---

## Package Tier Strategy Rationale

The 4-tier ladder exists for a specific strategic reason at each boundary:

### Tier 1: Abstractions-only (Free, no deps)

```xml
<!-- ModulusKit.Mediator.Abstractions has zero runtime dependencies -->
<PackageReference Include="ModulusKit.Mediator.Abstractions" />
<!-- No MassTransit, no FluentValidation, no Microsoft.Extensions.* -->
```

**Why:** Library authors and framework builders can reference the `ICommand`/`IQuery`/`Result<T>` contracts without dragging in any implementation. This maximizes surface area — any .NET library can become compatible with ModulusKit CQRS without taking a runtime dependency.

### Tier 2: Runtime packages (Full implementation)

```csharp
// AddModulusMediator() brings in FluentValidation, Microsoft.Extensions.DI, Microsoft.Extensions.Logging
// AddModulusMessaging() brings in MassTransit — explicit transport chosen by consumer
builder.Services.AddModulusMessaging(o => o.UseRabbitMq(connectionString));
// vs.
builder.Services.AddModulusMessaging(o => o.UseAzureServiceBus(connectionString));
// vs.
builder.Services.AddModulusMessaging(o => o.UseInMemory()); // for tests
```

**Why:** Transport selection at runtime DI registration (not at compile time or via abstractions package) means consumers aren't locked in. The messaging tier strategy is: make switching transports a one-line change — this is a key differentiator.

### Tier 3: DX packages (Compile-time)

```csharp
// Generators eliminate maintenance burden — every new handler is auto-registered
// Analyzers enforce the patterns that make the runtime tier work correctly
// Together they make the runtime tier "sticky" — teams that use Tier 3 rarely leave
services.AddModulusHandlers(); // Zero manual maintenance, compiler does the work
```

**Why:** The source generator is the highest-retention feature. Teams that rely on `AddModulusHandlers()` auto-registration can't easily migrate away without writing significant registration boilerplate.

### Tier 4: CLI (Acquisition)

```powershell
# One command creates a correctly-structured project
modulus init Acme.Commerce --aspire --transport rabbitmq
# Scaffolded project uses all 4 tiers by default
# Developer immediately sees value before writing a line of code
```

**Why:** The CLI is the acquisition engine. A developer who has never heard of ModulusKit runs `modulus init` and immediately has a working modular monolith. The CLI is not where money is made — it's where adoption starts.

---

## Positioning Against MediatR

MediatR is the incumbent. ModulusKit must win on three axes:

```markdown
| Axis | MediatR | ModulusKit |
|------|---------|-----------|
| License | Dual-license (commercial for enterprise) | MIT, always free |
| Registration | Manual `AddMediatR()` scanning | Source-generated, compile-time explicit |
| Result pattern | Not built-in | First-class `Result<T>`, enforced by analyzer |
| Messaging | Not included | Outbox/inbox with MassTransit, same package ecosystem |
| Scaffolding | None | `modulus init` creates a full working solution |
```

NEVER position ModulusKit as "MediatR but better" — position it as "the complete modular monolith starter kit that happens to include CQRS."

---

## WARNING: Premature Feature Gating

**The Problem:**

Splitting features across packages at too fine a granularity creates friction and reduces adoption.

```xml
<!-- BAD — imaginary over-segmentation -->
<PackageReference Include="ModulusKit.Mediator.Logging" />    <!-- ❌ -->
<PackageReference Include="ModulusKit.Mediator.Validation" /> <!-- ❌ -->
<PackageReference Include="ModulusKit.Mediator.Metrics" />    <!-- ❌ -->
```

**Why This Breaks:**
1. Developers install the wrong combination and spend hours debugging missing behaviors
2. Every new package is a new NuGet page, readme, version to manage
3. Install friction kills adoption — each additional required package is a decision point where developers abandon

**The Fix:**

```xml
<!-- GOOD — behaviors bundled in the runtime package, opted in via configuration -->
<PackageReference Include="ModulusKit.Mediator" />
```

```csharp
// Behaviors are registered explicitly but from one package
builder.Services.AddModulusMediator(o =>
{
    o.AddPipelineBehavior<ValidationBehavior<,>>();
    o.AddPipelineBehavior<LoggingBehavior<,>>();
    o.AddPipelineBehavior<MetricsBehavior<,>>();
});
```

---

## Versioning as a Strategic Lever

Semantic versioning communicates trust and stability. The 7-package ecosystem must move as one:

```powershell
# Verify all packages are at the same version before publishing
dotnet list Modulus.slnx package | Select-String "ModulusKit"
# Expected: all at identical version

# Check no package has a pre-release reference to another package
dotnet list Modulus.slnx package --include-prerelease
```

Breaking changes in `ModulusKit.Mediator.Abstractions` are the most costly — they require all downstream consumers to update. Prefer additive changes (new interfaces, new overloads) over breaking changes to the abstraction layer. Follow the Rule of Three: only add an abstraction when you have 3+ concrete uses for it.
