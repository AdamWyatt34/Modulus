# Conversion Optimization Reference

## Contents
- README Quick Start Optimization
- Package Description Copy
- Anti-Patterns in Developer Onboarding
- Upgrade Path Clarity

Conversion for ModulusKit means a developer: (1) installs a package, (2) wires it correctly, and (3) ships working code. Every friction point between `dotnet add package` and a passing test is a conversion killer.

---

## README Quick Start Optimization

The README quick start must show working code in under 60 seconds of reading. Developers decide whether to adopt a library in the first scroll.

```markdown
<!-- BAD — abstract, no concrete payoff -->
## Getting Started
ModulusKit provides a suite of abstractions and implementations for building modular .NET applications.
Install the packages and configure your services.

<!-- GOOD — immediate value, copy-pasteable -->
## 30-Second Start
dotnet add package ModulusKit.Mediator
dotnet add package ModulusKit.Generators
```

```csharp
// 1. Register
builder.Services.AddModulusMediator();
builder.Services.AddModulusHandlers(); // source-generated

// 2. Define
public record GetProductQuery(Guid Id) : IQuery<Product>;
public sealed class GetProductHandler(IProductRepo repo)
    : IQueryHandler<GetProductQuery, Product>
{
    public async Task<Result<Product>> Handle(GetProductQuery q, CancellationToken ct)
        => await repo.FindAsync(q.Id) ?? Error.NotFound("Product not found");
}

// 3. Use
var result = await mediator.Send(new GetProductQuery(id));
```

---

## Per-Package NuGet Descriptions

NuGet.org shows 200 characters of description in search results. Front-load the concrete value.

```xml
<!-- Directory.Build.props or individual .csproj -->

<!-- BAD — generic -->
<Description>Abstractions for the ModulusKit mediator library.</Description>

<!-- GOOD — specific value, tells who it's for -->
<Description>
  ICommand, IQuery, IStreamQuery, Result&lt;T&gt;, and Error types for custom CQRS mediator implementations.
  Zero runtime dependencies — safe for library authors to reference.
</Description>
```

```xml
<!-- ModulusKit.Generators description -->
<Description>
  Roslyn source generator that auto-registers all ICommandHandler, IQueryHandler,
  and IIntegrationEventHandler implementations at compile time. Eliminates manual DI wiring.
</Description>
```

---

## WARNING: Hiding the Tier Structure

**The Problem:**
```markdown
<!-- BAD — lists all 7 packages with no guidance -->
## Installation
- ModulusKit.Mediator.Abstractions
- ModulusKit.Mediator
- ModulusKit.Messaging.Abstractions
- ModulusKit.Messaging
- ModulusKit.Generators
- ModulusKit.Analyzers
- ModulusKit.Cli
```

**Why This Breaks:**
1. Developer paralysis — 7 packages with no guidance on which to pick
2. Over-installation — devs grab everything "just in case", creating unnecessary deps
3. Abstractions installed without runtime packages, causing runtime resolution failures

**The Fix:**
```markdown
## Installation

**Start here (most projects):**
```
dotnet add package ModulusKit.Mediator
dotnet add package ModulusKit.Messaging
dotnet add package ModulusKit.Generators
```

**Library authors (no runtime deps):**
```
dotnet add package ModulusKit.Mediator.Abstractions
dotnet add package ModulusKit.Messaging.Abstractions
```

**New project scaffold:**
```
dotnet tool install -g ModulusKit.Cli && modulus init MySolution
```

---

## Upgrade Path Clarity

Every new tier adoption must have a one-sentence migration description.

```markdown
<!-- In CHANGELOG or README Migration section -->

### Adding Generators (Tier 2 → Tier 3)
1. `dotnet add package ModulusKit.Generators`
2. Replace all manual `services.AddScoped<ICommandHandler<X>, Y>()` calls with `services.AddModulusHandlers()`
3. Build — the source generator emits the registrations at compile time

### Adding Analyzers (Tier 3 → Tier 3+)
1. `dotnet add package ModulusKit.Analyzers`
2. Build — MOD001–MOD005 warnings appear immediately
3. Fix any `throw` in handlers → `return Error.*` (MOD003 has a code fix)
```

---

## Checklist: README Conversion Audit

Copy and track:
- [ ] Quick start shows working code in ≤ 20 lines
- [ ] Package table explains which tier to install for each use case
- [ ] Each package has a concrete one-line value statement
- [ ] Migration path from Tier N to Tier N+1 is documented
- [ ] CLI `modulus init` example appears in the getting-started section
- [ ] NuGet package descriptions are ≤ 200 chars and front-load the value
