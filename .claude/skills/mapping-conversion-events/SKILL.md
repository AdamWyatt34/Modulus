All 7 files created at `.claude/skills/mapping-conversion-events/`:

**SKILL.md** — Overview of the 5-stage developer adoption funnel (Discovery → Evaluation → Trial → Activation → Adoption), with the key insight that activation = `modulus init` exit code 0 + `dotnet build` success.

**references/**:
- **conversion-optimization.md** — Funnel stage definitions, activation criteria, the three main drop-off points (invalid name, missing Aspire workload, bad transport value), and CLI output checklist
- **content-copy.md** — Four copy surfaces mapped to funnel stages (NuGet `<Description>`, `--help` text, `IConsoleOutput`, README Quick Start) with DO/DON'T examples
- **distribution.md** — Three channels (NuGet, GitHub, docs site), `<PackageTags>` patterns, release checklist, and the CI tag-gating requirement
- **measurement-testing.md** — Why NuGet downloads ≠ activation, funnel metrics table, xUnit test patterns asserting both exit codes AND console output, validate→build feedback loop
- **growth-engineering.md** — Passive distribution via scaffolded `ModulusKit.*` references, `AddModulusHandlers()` as attribution, abstractions packages dependency budget, analyzer rule IDs as acquisition channel
- **strategy-monetization.md** — Package tier ladder (Abstractions → Implementation → DX → CLI), future paid tier candidates, retention by design (source generator + Result pattern coupling), semver trust contract