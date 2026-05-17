# Measurement and Testing Reference

## Contents
- What to Measure for a Developer Library
- Available Signals
- Funnel Analysis by Surface
- Testing Copy Changes
- Anti-Patterns

---

## What to Measure for a Developer Library

ModulusKit has no web analytics on the install path — conversion happens in a terminal. Proxy metrics substitute:

| Proxy Metric | What It Signals | Where to Find It |
|-------------|----------------|-----------------|
| NuGet download count | Top-of-funnel reach | nuget.org package stats |
| GitHub stars | Trust / perceived quality | GitHub repo insights |
| GitHub traffic (clones vs views) | README-to-install conversion | GitHub Insights → Traffic |
| Docs site visits | Docs landing interest | VitePress analytics (if configured) |
| CLI telemetry | install-to-scaffold rate | Would require opt-in instrumentation |
| GitHub Issues "Getting Started" | Onboarding friction | Issue labels |

**WARNING**: No analytics are currently wired to `docs/` (VitePress). Adding Plausible or Umami (privacy-friendly, no cookies) would unlock funnel visibility without GDPR friction.

## Available Signals

### NuGet Download Trends

NuGet exposes download stats per package at:
```
https://www.nuget.org/stats/packages/ModulusKit.Cli?groupby=Version
```

Track week-over-week for each package separately — a spike in `ModulusKit.Mediator.Abstractions` without a corresponding spike in `ModulusKit.Mediator` indicates developers are adopting abstractions but not the full implementation (possibly using their own mediator).

### GitHub Repository Traffic

```
GitHub → Insights → Traffic
```

Key ratios:
- **Unique visitors / Clones** → What % of README readers install the CLI
- **Referring sites** → Which channels drive quality traffic (NuGet.org referrals convert better than social)

### GitHub Issue Signals

Issues tagged `question` or `help wanted` with titles containing "getting started", "install", or "first run" directly indicate documentation friction. Search:

```bash
gh issue list --label "question" --search "getting started OR install OR init"
```

Each cluster of similar questions = a friction point on the landing journey worth fixing.

## Funnel Analysis by Surface

### README Funnel

The README-to-install funnel has no tracking, but GitHub Traffic shows `git clone` events. A rough proxy:

```
GitHub README views (Traffic → Views)
  ÷ NuGet download delta for same period
  = README conversion rate (rough)
```

If this ratio degrades after a README change, revert and test a different structure.

### Docs Funnel (if Plausible/Umami added)

```
docs/index.md pageviews
  → /getting-started/ pageviews        (hero CTA conversion)
    → /getting-started/ time-on-page   (do they read the whole page?)
      → NuGet download event (external link click tracking)
```

To add Plausible to VitePress:

```js
// docs/.vitepress/config.ts
export default defineConfig({
  head: [
    ['script', {
      defer: '',
      'data-domain': 'moduluskit.dev',
      src: 'https://plausible.io/js/script.js'
    }]
  ]
})
```

### CLI Funnel

The gap between `dotnet tool install -g ModulusKit.Cli` (NuGet download) and a user successfully running `modulus init` is invisible without telemetry. Signs of friction:

- GitHub issues: "modulus command not found"
- Issues: "init fails on first run"
- Issues: "what is --transport for?"

These map to specific copy fixes in CLI `--help` text or `IConsoleOutput` messages.

## Testing Copy Changes

### A/B Testing Without Infrastructure

For a developer library with no web UI, "A/B testing" means versioned changes with before/after download rate comparison:

1. Change one surface at a time (e.g., `<Description>` for one package only)
2. Wait 2 weeks for NuGet download data to normalize
3. Compare week-over-week delta against previous 2-week baseline
4. Only change the next surface after seeing signal

This is slow but reliable. Changing multiple surfaces simultaneously makes causation impossible to determine.

### Copy Validation Checklist

Before shipping a copy change to any surface:

```
- [ ] First sentence states outcome, not feature name
- [ ] Differentiator ("no MediatR") present in first 80 chars (NuGet) or first paragraph (README)
- [ ] Every code block compiles (test locally with dotnet build)
- [ ] Install command matches the canonical: dotnet tool install -g ModulusKit.Cli
- [ ] Package name uses ModulusKit.* (not Modulus.*)
- [ ] No placeholder text [TODO] or [coming soon]
```

## Anti-Patterns

### WARNING: Treating Star Count as the Primary Metric

**The Problem:**
Stars measure interest, not adoption. A project can have 10k stars and 50 weekly installs. Download count per week is the metric that reflects actual library usage.

**Why This Matters:**
Optimizing for stars (writing viral social posts) is a different strategy than optimizing for installs (improving NuGet discoverability and README clarity). Don't conflate them.

### WARNING: Changing Multiple Surfaces Simultaneously

**The Problem:**
If README, `docs/index.md`, and 3 NuGet descriptions all change in the same release, a download rate change is unattributable.

**The Fix:**
Ship copy improvements in isolated PRs. Tag them in CHANGELOG.md so download rate changes can be correlated with specific changes in retrospect.

For instrumentation patterns if analytics are added, see the **mapping-conversion-events** skill.
