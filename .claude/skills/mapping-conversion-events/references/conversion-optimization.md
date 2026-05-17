# Conversion Optimization Reference

## Contents
- Funnel Stage Definitions
- Activation Criteria
- Drop-Off Points and Fixes
- CLI UX as Conversion Surface
- Anti-Patterns

---

## Funnel Stage Definitions

For ModulusKit, "conversion" = a developer successfully scaffolding and building a solution. Define each stage precisely before optimizing:

```
Discovery   →  Evaluation  →  Trial         →  Activation       →  Adoption
NuGet view     README read    tool install     modulus init +       add-module 2+
GitHub visit   docs browse    (download)       dotnet build 0       messaging config
```

**Activation is the only binary signal**: exit code 0 from `InitHandler` after scaffold + the developer can `dotnet build` without errors.

---

## Activation Criteria

Activation = **all three** must be true:
1. `modulus init <name>` exits with code 0
2. Generated solution compiles (`dotnet build` succeeds)
3. At least one `modulus add-module` has been called

Point 3 is where most tools stop tracking. A scaffold with zero modules is not an activated user.

```csharp
// src/Modulus.Cli/Handlers/InitHandler.cs
// Activation moment: print the minimum viable next step, not a wall of options
_console.WriteLine("✓ Solution scaffolded.");
_console.WriteLine();
_console.WriteLine("Add your first module:");
_console.WriteLine($"  cd {solutionName}");
_console.WriteLine("  modulus add-module Catalog");
```

---

## Drop-Off Points and Fixes

### Drop-Off 1: Invalid solution name

**Where:** `CSharpIdentifierValidator` in `src/Modulus.Cli/Validation/`

**Fix:** Error message must include a valid example, not just a rule:

```csharp
// BAD — leaves developer guessing what's valid
_console.WriteError($"'{solutionName}' is not a valid C# identifier.");

// GOOD — unblocks immediately
_console.WriteError(
    $"'{solutionName}' is not a valid C# identifier. " +
    $"Try: modulus init {ToPascalCase(solutionName)}");
```

### Drop-Off 2: Aspire not installed

**Where:** `IProcessRunner` in `InitHandler` when `--aspire` is passed

**Fix:** Detect and surface a fixable error before generation starts:

```csharp
// Check precondition before generating files — don't leave a half-created scaffold
if (aspire && !await _processRunner.IsInstalledAsync("dotnet-aspire"))
{
    _console.WriteError("Aspire workload not found.");
    _console.WriteLine("  dotnet workload install aspire");
    return 1; // hard stop — partial scaffold would be worse
}
```

### Drop-Off 3: Transport mismatch

**Where:** `--transport` option in `src/Modulus.Cli/Commands/InitCommand.cs`

**Fix:** Validate transport value early and show available options:

```csharp
// In command definition — fail fast with the valid options listed
var transportOption = new Option<string>(
    "--transport",
    () => "inmemory",
    "Message transport: inmemory | rabbitmq | azureservicebus");
```

---

## CLI UX as Conversion Surface

The terminal is the only UI. Every `IConsoleOutput` call is a conversion surface.

**Checklist for activation-moment output:**

```
- [ ] Confirm what was created (✓ with name)
- [ ] Show the single most important next command
- [ ] Surface docs link only if something unusual was configured
- [ ] Never dump a full feature list after success
```

```csharp
// GOOD — focused activation-moment output
_console.WriteLine($"✓ {solutionName} created.");
_console.WriteLine();
_console.WriteLine($"  cd {solutionName}");
_console.WriteLine("  modulus add-module <ModuleName>");

// BAD — information overload kills activation momentum
_console.WriteLine($"✓ {solutionName} created.");
_console.WriteLine("Features included: CQRS mediator, outbox pattern, Aspire integration,");
_console.WriteLine("FluentValidation pipeline, logging behaviors, metrics behaviors,");
_console.WriteLine("source generators for handler registration...");
```

---

## Anti-Patterns

### WARNING: Treating Download Count as Activation

**The Problem:** NuGet downloads spike on every new version release (bots, caches, CI restores). Downloads ≠ active users.

**Why This Breaks:** You'll optimize for the wrong metric. A package can have 10,000 downloads and 5 real users.

**The Fix:** Track `modulus init` exit code 0 as the first real signal. Downloads are reach, not conversion.

### WARNING: Surfacing Errors After Partial Generation

**The Problem:**

```csharp
// BAD — generates files, then fails on process step
await GenerateFilesAsync(solutionName);
var result = await _processRunner.RunAsync("dotnet", "build"); // fails here
if (!result.IsSuccess) return 1; // directory exists but is broken
```

**Why This Breaks:** Developer now has a partial scaffold they have to clean up manually. This is a high-churn moment — many developers abandon at this point.

**The Fix:** Validate all preconditions before writing any files. Use the validate → check → generate order from `InitHandler`.

### WARNING: Omitting Exit Code Documentation from README

The README's Quick Start must show what success looks like. If a developer can't tell whether `modulus init` worked, they'll assume it failed.

```markdown
<!-- GOOD — shows expected output -->
```bash
modulus init EShop --aspire
# ✓ EShop created.
#   cd EShop
#   modulus add-module Catalog
```
```
