# Growth Engineering Reference

## Contents
- CLI as Growth Engine
- Template Quality as Retention
- Contribution Flywheel
- Anti-Patterns in OSS Growth
- GitHub Repository Signals

For an open-source library, growth engineering means: (1) making the Tier 4 CLI experience so good that developers share it, (2) making scaffolded code good enough that teams standardize on it, and (3) making contribution easy enough that the community extends the ladder.

---

## CLI as Growth Engine

The `modulus init` command is the highest-leverage growth surface. A developer who runs it and gets a working project in 30 seconds tells their team. One CLI run can convert an entire team from Tier 0 to Tier 4.

```powershell
# The growth loop entry point
dotnet tool install -g ModulusKit.Cli
modulus init Acme.Platform --aspire --transport rabbitmq
cd Acme.Platform
dotnet build  # Must succeed with zero modifications
dotnet test   # Must pass with zero modifications
```

If `dotnet build` fails after `modulus init`, the growth loop is broken. Every scaffolding regression is a growth regression.

---

## Template Quality Signals

Scaffolded templates live in `src/Modulus.Templates/`. They are the product promise. A developer who runs `modulus init` and sees bad patterns in the generated code immediately loses trust in the entire ecosystem.

```csharp
// The generated sample handler MUST demonstrate best practices
// src/Modulus.Templates/Handlers/SampleCommandHandler.scriban

// GOOD — generated handler demonstrates the Result pattern
public sealed class Create{{ module_name }}CommandHandler(
    I{{ module_name }}Repository repo)
    : ICommandHandler<Create{{ module_name }}Command>
{
    public async Task<Result> Handle(Create{{ module_name }}Command cmd, CancellationToken ct)
    {
        var existing = await repo.FindByNameAsync(cmd.Name, ct);
        if (existing is not null)
            return Error.Conflict("{{ module_name }} with that name already exists");

        await repo.AddAsync(new {{ module_name }}(cmd.Name), ct);
        return Result.Success();
    }
}
```

```csharp
// BAD — generated handler that throws exceptions defeats the library's own promise
public async Task<Result> Handle(Create{{ module_name }}Command cmd, CancellationToken ct)
{
    var existing = await repo.FindByNameAsync(cmd.Name, ct);
    if (existing is not null)
        throw new InvalidOperationException("Already exists"); // ❌ MOD003 violation in generated code!
}
```

---

## Contribution Flywheel

The contribution flywheel: good docs → easy to contribute → more templates → more adoption → more contributors.

Key friction points to eliminate:

```markdown
<!-- CONTRIBUTING.md must answer these in order -->
1. How do I add a new CLI command?
   → Commands/MyCommand.cs + Handlers/MyCommandHandler.cs + register in Program.cs

2. How do I add a new Scriban template?
   → src/Modulus.Templates/ + embed as EmbeddedResource + reference in handler

3. How do I test my change?
   → dotnet test Modulus.slnx --filter "FullyQualifiedName~Cli"

4. What's the PR checklist?
   → Build passes, tests pass, new template has a test in Modulus.Cli.Tests
```

---

## GitHub Repository Signals

These signals indicate offer-ladder health:

| Signal | Healthy | Problem |
|--------|---------|---------|
| Issues tagged `help-wanted` have PRs within 30d | Yes | Contribution friction |
| `good-first-issue` count | 3-5 open | 0 = no entry point for contributors |
| README Quick Start tested in CI | Yes | Drift between docs and reality |
| `modulus init` integration test in CI | Yes | Scaffold regressions undetected |

---

## WARNING: Template Drift

**The Problem:**

Templates in `src/Modulus.Templates/` reference package names, API shapes, and patterns that can become stale when the library evolves.

```csharp
// BAD — template hard-codes an API that was refactored in v1.2
// Template still says AddModulusMediator(config => config.AddBehavior<>())
// but the real API is now AddPipelineBehavior<>() as a separate call
```

**Why This Breaks:**
1. `modulus init` generates code that doesn't compile against the current packages
2. Developers blame the CLI, not the template — trust erodes across the entire ecosystem
3. Growth loop breaks: no one shares a tool that generates broken code

**The Fix:**

```csharp
// Integration test that runs modulus init and verifies the output compiles
[Fact]
public async Task InitHandler_GeneratedCode_CompilesAgainstCurrentPackages()
{
    var fs = new FakeFileSystem();
    var handler = new InitHandler(fs, new FakeProcessRunner(), new FakeConsole());
    await handler.ExecuteAsync("Test", aspire: false, transport: "inmemory");

    // Verify generated Program.cs uses current API surface
    var programCs = fs.ReadFile("Test/src/Test.Api/Program.cs");
    programCs.ShouldContain("AddModulusMediator()");
    programCs.ShouldContain("AddPipelineBehavior<ValidationBehavior<,>>()");
}
```

See the **system-commandline** skill for CLI handler test patterns and the **xunit** skill for test project setup.
