All 7 files created at `.claude/skills/inspecting-search-coverage/`:

- **SKILL.md** — Quick start audit commands, current gap summary, copyable checklist
- **references/technical.md** — `.csproj` metadata fields, shared vs. per-package, fixes for Analyzers/Generators gaps
- **references/on-page.md** — NuGet listing anatomy, README structure, tag strategy per package
- **references/content.md** — XML doc generation, priority order (Abstractions first), patterns for `<summary>`, `<example>`, `<remarks>`
- **references/programmatic.md** — PowerShell audit scripts, CI metadata validation, NuGet pack verification workflow
- **references/schema.md** — Full `.csproj` schema, current state table per package (with ✅/⚠️/❌), symbol package support
- **references/competitive.md** — Competitor landscape, keyword gaps, differentiation messaging, cross-linking anti-pattern

Key findings captured in the skill based on actual `.csproj` inspection:
- `ModulusKit.Analyzers` is missing `<PackageReadmeFile>`, `<PackageLicenseExpression>`, and `<PackageProjectUrl>`
- `ModulusKit.Generators` description references "StronglyTypedId" which doesn't match its actual function
- No `<PackageIcon>` or `<Copyright>` on any package
- All packages have sparse tags missing high-value terms like `modular-monolith`, `transactional-outbox`, `aspire`