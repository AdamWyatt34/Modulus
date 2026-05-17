# Growth Engineering Reference

## Contents
- Growth loops for OSS developer tools
- README → docs → CLI install loop
- Community and ecosystem loops
- Contributing friction reduction
- IConsoleOutput as a growth surface
- Anti-patterns

---

## Growth Loops for OSS Developer Tools

ModulusKit's growth is driven by developer discovery, not paid channels. Two primary loops:

**Loop 1: Search → Use → Share**
```
NuGet/GitHub search → README → install CLI → scaffold project → share with team
```

**Loop 2: Problem → Docs → Adopt**
```
"how to build modular monolith .NET" (Google) → docs site → getting-started → install
```

Both loops depend on discoverability (see [distribution.md](distribution.md)) and on the
README converting visitors to installers. Optimize the loops, not the individual surfaces.

## README → Docs → CLI Install Loop

The loop works when each surface has exactly one next action:

```markdown
<!-- README: next action = go to docs -->
> **[Read the full documentation](https://adamwyatt34.github.io/Modulus/)**

<!-- docs/index.md: next action = get started -->
actions:
  - theme: brand
    text: Get Started
    link: /getting-started/

<!-- docs/getting-started/: next action = install CLI -->
\`\`\`bash
dotnet tool install --global ModulusKit.Cli
\`\`\`
```

NEVER present multiple primary CTAs on the same page. One page = one next step.

## Community and Ecosystem Loops

### GitHub Discussions as a Signal Loop

Enable GitHub Discussions with a "Show & Tell" category. Developers who share projects built
with ModulusKit generate social proof. The copy for creating this section:

```markdown
## Show & Tell
Share what you've built with ModulusKit. Project name, module count, and what made it easier.
```

### Recipes as a Discovery Surface

`docs/recipes/` pages rank for long-tail searches ("modular monolith outbox pattern .NET").
Each recipe drives new visitors into the install loop. When adding a recipe:

```markdown
<!-- recipes/outbox-pattern.md -->
---
title: Implementing the Transactional Outbox Pattern with ModulusKit
description: Step-by-step guide to reliable cross-module event publishing using ModulusKit.Messaging transactional outbox.
---
```

The title and description are the SEO hook. Make them match what developers actually search.

## Contributing Friction Reduction

Contributors are a growth multiplier — they add features, write docs, and generate word-of-mouth.
The `CONTRIBUTING.md` and `docs/contributing/` content must show time-to-first-PR in ≤5 steps:

```markdown
<!-- GOOD — concrete path, numbered, ends with feedback loop -->
## Your First Contribution

1. Fork and clone the repo
2. `dotnet build Modulus.slnx` — verify it builds
3. Run `dotnet test Modulus.slnx` — all tests pass
4. Make your change
5. Open a PR — CI will validate automatically

<!-- BAD — vague, no success criteria -->
## Contributing
Please read our contribution guidelines and make sure to follow the code style before opening a PR.
```

## IConsoleOutput as a Growth Surface

The CLI's console output is shown to every user on every scaffold. Use it to drive awareness
of lesser-known features and docs:

```csharp
// GOOD — surface docs link and next steps after scaffold
console.WriteLine("Solution created: EShop/");
console.WriteLine("");
console.WriteLine("Next steps:");
console.WriteLine("  cd EShop");
console.WriteLine("  dotnet run --project src/EShop.WebApi");
console.WriteLine("");
console.WriteLine("Docs: https://adamwyatt34.github.io/Modulus/getting-started/");

// BAD — no next step, user is left guessing
console.WriteLine("Done.");
```

The post-scaffold message is the highest-frequency marketing touchpoint — every user sees it.
Make it count.

## WARNING: Over-Engineering the Growth Surface

**The Problem:** Adding complex analytics, badge systems, or "star us on GitHub" prompts to
an OSS CLI tool.

**Why This Breaks:** Developer audiences immediately distrust tools that feel promotional.
The fastest path to growth is making the tool excellent, the docs clear, and the install path
frictionless. Manufactured virality (star prompts, share buttons) destroys credibility.

**The Fix:** Focus growth engineering on:
1. Reducing time-to-first-running-app (remove install steps)
2. Making recipes/docs rank in search (keyword-rich headings)
3. Making contributing approachable (5-step path to first PR)

## Growth Checklist

Copy and track progress:
- [ ] README has exactly one primary CTA (`Get Started` link to docs)
- [ ] `docs/getting-started/` shows running app in ≤3 steps
- [ ] CLI post-scaffold output includes docs link
- [ ] `docs/recipes/` page titles include target search phrases
- [ ] `CONTRIBUTING.md` shows path to first PR in ≤5 steps
- [ ] GitHub Discussions "Show & Tell" category is enabled
