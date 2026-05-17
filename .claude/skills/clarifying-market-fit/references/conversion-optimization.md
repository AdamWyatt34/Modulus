# Conversion Optimization Reference

## Contents
- Developer "conversion" funnel for open-source tools
- README hero optimization
- Docs getting-started friction audit
- NuGet search conversion
- Anti-patterns

---

## Developer "Conversion" Funnel

For ModulusKit, "conversion" means: **GitHub visitor → installs CLI → creates first solution**.

```
NuGet/GitHub search → README → docs/getting-started/ → installs CLI → runs `modulus init`
```

Every surface must reduce friction to the next step. NEVER let a page be a dead end — every
section needs an obvious "what next" link.

## README Hero Optimization

The first 20 lines of `README.md` are the entire pitch for 80% of visitors. Make them count.

```markdown
<!-- GOOD — outcome → differentiator → single CTA -->
# ModulusKit

Scaffold production-ready .NET modular monoliths in seconds.
Includes a custom CQRS mediator (no MediatR), transactional outbox, and Aspire integration.

\`\`\`bash
dotnet tool install --global ModulusKit.Cli
modulus init EShop --aspire --transport rabbitmq
\`\`\`
```

```markdown
<!-- BAD — abstract description, no code, no outcome -->
# ModulusKit

A modular library ecosystem for scaffolding .NET solutions with CQRS and event-driven messaging.
```

**Why it matters:** Developers spend 8–15 seconds on a README before bouncing. If they don't
see working code or a clear outcome immediately, they leave.

## Docs Getting-Started Friction Audit

`docs/getting-started/` is where evaluators decide to commit. Run this audit regularly:

```bash
# Check for broken links in getting-started
grep -r "\[.*\](.*)" docs/getting-started/ --include="*.md"

# Count steps to first working app
grep -c "^##\|^###" docs/getting-started/index.md
```

**Target:** 3 steps or fewer from install to `dotnet run` on a generated solution.

```markdown
<!-- GOOD — 3 steps, code at every step -->
## Get Started

### 1. Install
\`\`\`bash
dotnet tool install --global ModulusKit.Cli
\`\`\`

### 2. Scaffold
\`\`\`bash
modulus init EShop --aspire
cd EShop
\`\`\`

### 3. Run
\`\`\`bash
dotnet run --project src/EShop.WebApi
\`\`\`
```

## NuGet Search Conversion

NuGet shows `<Description>` truncated to ~100 characters in search results. Optimize for the
truncation point:

```xml
<!-- GOOD — key value prop before truncation -->
<Description>Lightweight CQRS mediator for .NET. Pipeline behaviors, Result pattern,
FluentValidation integration. No MediatR dependency.</Description>

<!-- BAD — generic, no differentiator in first 100 chars -->
<Description>A mediator implementation that provides command and query handling with
pipeline behaviors for .NET applications using a clean architecture approach.</Description>
```

**Also optimize tags** — `modular-monolith` is a high-intent search term:

```xml
<!-- All packages should include modular-monolith for cross-discovery -->
<PackageTags>mediator;cqrs;result-pattern;modular-monolith;pipeline</PackageTags>
```

## WARNING: Missing Time-to-Value Signal

**The Problem:**
```markdown
<!-- BAD — no indication of how fast setup is -->
## Installation
Install ModulusKit.Cli globally using the dotnet tool install command...
```

**Why This Breaks:** Developers evaluating tools need to know the cost of trying it. If setup
looks long, they won't start. The actual setup is ~60 seconds — say so.

**The Fix:**
```markdown
## Get Running in 60 Seconds
\`\`\`bash
dotnet tool install --global ModulusKit.Cli  # ~5s
modulus init MyApp --aspire                   # ~30s scaffold
dotnet run --project src/MyApp.WebApi         # ~25s first build
\`\`\`
```

## Conversion Validation Loop

1. Read `README.md` from the perspective of a developer who has never heard of the project
2. Check: does the first code block show a complete, runnable command?
3. Check: is there a link to `docs/getting-started/` in the first 30 lines?
4. If either fails, fix before merging changes to `README.md`
