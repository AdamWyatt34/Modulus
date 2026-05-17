Three files created at `.claude/skills/ef-core/`:

**`SKILL.md`** — Quick overview with the two DbContexts, key store types, and 4 code examples covering schema definition, composite keys, InMemory test setup, and idempotent save.

**`references/patterns.md`** — Deep patterns covering:
- Why DbContexts must stay separate (OutboxDbContext vs InboxDbContext)
- Entity model design (sealed records, init-only, AssemblyQualifiedName for type resolution)
- Store implementation with `ExecuteUpdateAsync` for batch updates
- Per-handler idempotency via `InboxMessageConsumers` composite key
- Three documented anti-patterns: singleton DbContext, missing `ChangeTracker.Clear()`, and short type names that break `Type.GetType()`

**`references/workflows.md`** — Step-by-step workflows with copyable checklists:
- Adding a new store (6-step sequence with code for each step)
- InMemory test patterns: unique-db-per-test vs `InMemoryDatabaseRoot` for cross-scope, plus InMemory limitation warnings
- DI registration: why Modulus doesn't call `AddDbContext` itself
- New entity checklist (9 items) with validation command