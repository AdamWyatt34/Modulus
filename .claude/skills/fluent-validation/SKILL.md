Three files created at `.claude/skills/fluent-validation/`:

- **SKILL.md** — overview, quick-start patterns for command/query validators, pipeline registration, key concepts table, and links to references
- **references/patterns.md** — validator structure, error mapping internals, DO/DON'T pairs (sealed classes, no DB calls in validators, no manual registration), two WARNING sections with anti-pattern + fix format, and source generator discovery rules
- **references/workflows.md** — end-to-end checklist for adding a validator, three test patterns (failure path, no-validators, typed result), debugging guide with iterate-until-pass steps, and multiple-validators-per-request pattern with aggregation test