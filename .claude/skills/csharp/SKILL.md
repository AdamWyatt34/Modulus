Three files created at `.claude/skills/csharp/`:

**`SKILL.md`** — Quick reference with Result pattern returns, record/interface markers, sealed primary-constructor classes, error factory selection table, and `Match` pattern.

**`references/patterns.md`** — Deep coverage of:
- Result/Result\<T\> with implicit conversions (with exact file references to the source)
- Records + interface markers (ICommand, IQuery, IIntegrationEvent, IDomainEvent)
- Sealed classes + primary constructors with a DO/DON'T pair
- Implicit conversion caveats with async/Task.FromResult
- Three documented anti-patterns: throwing for expected errors, mutable commands, manual handler registration

**`references/workflows.md`** — Step-by-step checklists for:
- Adding a command handler (with iterate-until-pass build validation)
- Adding a query handler (including streaming query caveat)
- Adding a pipeline behavior (with ordering explanation)
- Adding a CLI command (factory pattern + test double example)
- Running/filtering tests
- Format and analyzer validation before committing