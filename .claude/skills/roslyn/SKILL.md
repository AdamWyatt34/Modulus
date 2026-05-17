Three files created at `.claude/skills/roslyn/`:

**SKILL.md** — quick reference with all 3 generators and 5 analyzers at a glance, quick-start examples for `[StronglyTypedId]`, adding a diagnostic, and testing both generators and analyzers.

**references/patterns.md** — covers:
- `IIncrementalGenerator` pipeline structure with `static` predicate requirement
- Handler discovery via `AllInterfaces` + base-type walk for `AbstractValidator<T>`
- Equatable struct pattern (with WARNING on returning `INamedTypeSymbol` directly)
- `CompilationStartAction` for scoped analyzers (MOD004 domain-only pattern)
- `SyntaxNodeAction` vs `SymbolAction` decision
- Code fix provider skeleton with `GetFixAllProvider()`
- `[StronglyTypedId]` requirements and what gets generated

**references/workflows.md** — covers:
- Step-by-step checklists for adding a generator and adding an analyzer+fix
- Generator skeleton and analyzer skeleton code
- Full test examples using `GeneratorTestHelper` and `AnalyzerTestHelper`
- Cross-module test setup for MOD001
- Handler discovery debugging checklist
- Complete diagnostics quick reference table (MODGEN + MOD)