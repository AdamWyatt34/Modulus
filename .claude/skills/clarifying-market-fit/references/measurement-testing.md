# Measurement & Testing Reference

## Contents
- What to measure for an OSS developer tool
- GitHub and NuGet metrics
- Docs site signals
- CLI usage instrumentation options
- Testing messaging copy with real developers
- Anti-patterns

---

## What to Measure for an OSS Developer Tool

ModulusKit has no SaaS analytics. Measure adoption signals instead:

| Signal | Source | Meaning |
|--------|--------|---------|
| NuGet download count | NuGet stats API | Absolute adoption volume |
| NuGet download trend | NuGet stats API | Growth velocity |
| GitHub stars | GitHub API | Awareness/interest |
| GitHub forks | GitHub API | Active adoption intent |
| `dotnet tool search modulus` rank | NuGet search | Discoverability for new users |
| Docs page views | GitHub Pages / VitePress analytics | Content resonance |
| GitHub issues with "question" label | GitHub | Friction points in messaging |

## Checking NuGet Download Stats

```powershell
# NuGet stats API — daily/monthly downloads per package
$package = "ModulusKit.Cli"
Invoke-RestMethod "https://api.nuget.org/v3/registration5/$($package.ToLower())/index.json" `
  | Select-Object -ExpandProperty items `
  | Select-Object -Last 1 `
  | Select-Object -ExpandProperty items `
  | Select-Object catalogEntry `
  | ForEach-Object { $_.catalogEntry.version }
```

For download counts, use the NuGet stats endpoint directly or the NuGet Gallery UI.

## Docs Site Signals

`docs/` is a VitePress static site. Add basic analytics to understand which pages developers
read before leaving — this identifies where messaging is failing:

```javascript
// docs/.vitepress/config.ts
export default {
  head: [
    // Add analytics script tag here
    // Plausible is GDPR-compliant and open source — good fit for OSS projects
    ['script', { 'data-domain': 'yourdomain.github.io', src: 'https://plausible.io/js/script.js' }]
  ]
}
```

**Pages with high exit rate = messaging or content gaps.** If `getting-started/` has high
exits, the copy isn't converting evaluators. If `mediator/` exits are high, the value prop
isn't clear.

## CLI Usage Instrumentation

The CLI has no telemetry by default. NEVER add opt-out telemetry without explicit user consent —
this causes backlash in open-source communities. If telemetry is added, it must:

```csharp
// GOOD — opt-in only, clearly documented
var telemetryOption = new Option<bool>(
    "--telemetry",
    "Send anonymous usage statistics to help improve ModulusKit (default: false)");

// BAD — hidden telemetry without disclosure
// Never instrument without user knowledge
```

The most useful proxy for CLI adoption is NuGet download count for `ModulusKit.Cli`.

## Testing Messaging Copy with Real Developers

Messaging quality for a developer tool is testable through GitHub issues and discussions:

```bash
# Count "how do I" issues — indicates unclear docs/messaging
gh issue list --label question --repo adamwyatt34/Modulus --json title | \
  Select-String "how do\|what is\|where is"

# Count issues closed as "won't fix" = scope confusion
gh issue list --label "wont-fix" --repo adamwyatt34/Modulus --state closed
```

**Scope confusion signals** — developers asking about use cases that aren't in the ICP:
- "Does this support microservices?" → README positioning is unclear
- "Does this work with MediatR?" → differentiator copy needs to be stronger
- "What's the difference between Modulus.Mediator and MediatR?" → comparison table is missing

## Testing Package Descriptions

NuGet description A/B testing isn't possible (no built-in tool), but you can validate by:

1. Search NuGet for `modular-monolith dotnet` and check if ModulusKit appears in results
2. Verify description isn't truncated at a bad point by viewing on NuGet.org
3. Check that `<PackageTags>` match what developers type in search

```bash
# Verify tags include modular-monolith on all packages
grep -r "<PackageTags>" src/ --include="*.csproj" | grep -v "modular-monolith"
# Any output = missing tag, needs fixing
```

## WARNING: Vanity Metrics for OSS Tools

**The Problem:** Tracking GitHub stars as the primary success metric.

**Why This Breaks:** Stars measure interest, not adoption. A project can have 500 stars and
10 actual users. NuGet downloads + return usage (version bumps, forks with commits) is a
more honest signal of real adoption.

**The Fix:** Weight NuGet monthly downloads and GitHub "used by" count (package dependency
graph) over star count when evaluating messaging effectiveness.
