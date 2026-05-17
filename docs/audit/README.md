# Modulus Audit (2026-05-17)

Code review, fix plan, and backlog for the Modulus library ecosystem.

| Document | Purpose |
|----------|---------|
| [`code-review.md`](code-review.md) | Full code review — Top 10 issues + architecture, .NET language quality, security, template, CLI, generator, analyzer findings with severity tags and file:line references |
| [`top-10-fixes-plan.md`](top-10-fixes-plan.md) | Five sequenced PRs (PR1 templates, PR2 UnitOfWorkBehavior, PR3 CLI hardening, PR4 messaging hardening, PR5 supply chain) with code shape, tests, and verification per PR |
| [`missing-features-plan.md`](missing-features-plan.md) | Tier 1-3 backlog (PR6-PR20) covering E2E tests, per-package versioning, EF migrations, observability, benchmarks, governance |

## Decisions locked in

- **UnitOfWorkBehavior** — ship in `Modulus.Mediator`; template's local copies deleted.
- **MassTransit** — stay on v7.3.1 (v8 went paid). Pin + document + CVE scan.
