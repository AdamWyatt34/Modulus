# Content Copy Reference

## Contents
- Copy Surfaces in ModulusKit
- NuGet Package Descriptions
- CLI Help Text and Output
- README Conversion Copy
- Anti-Patterns

---

## Copy Surfaces in ModulusKit

ModulusKit has four copy surfaces that directly affect funnel conversion. Each maps to a stage:

| Surface | File | Stage |
|---------|------|-------|
| NuGet `<Description>` | `.csproj` files | Discovery → Trial |
| `--help` output | `src/Modulus.Cli/Commands/` | Trial → Activation |
| `IConsoleOutput` messages | `src/Modulus.Cli/Handlers/` | Activation |
| README Quick Start | `README.md` | Evaluation → Trial |

See the **crafting-page-messaging** skill for detailed copy patterns per surface.

---

## NuGet Package Descriptions

The `<Description>` field in each `.csproj` is the first copy a developer reads on NuGet.org. It must answer "what does this do and why should I use it" in two sentences.

```xml
<!-- src/Modulus.Cli/Modulus.Cli.csproj — lead with the action -->
<Description>
  Scaffold .NET modular monolith solutions in seconds.
  Generates CQRS mediator, transactional outbox, and optional Aspire integration from one command.
</Description>
```

```xml
<!-- src/Modulus.Mediator/Modulus.Mediator.csproj — lead with the differentiator -->
<Description>
  Lightweight CQRS mediator with pipeline behaviors and Result pattern. No MediatR dependency.
  Drop-in for .NET modular monolith command/query dispatch.
</Description>
```

```xml
<!-- src/Modulus.Messaging/Modulus.Messaging.csproj — lead with the guarantee -->
<Description>
  MassTransit abstraction with transactional outbox for reliable cross-module event publishing.
  Supports RabbitMQ, Azure Service Bus, and in-memory transports.
</Description>
```

**Rule:** Every description must include what it replaces or why it's different. "CQRS mediator" is generic; "No MediatR dependency" is a differentiator that converts.

---

## CLI Help Text and Output

`--help` text is read by developers in the evaluation-to-trial gap. It must show a working example, not a feature list.

```csharp
// src/Modulus.Cli/Commands/InitCommand.cs
// Include a concrete example in the command description
var command = new Command(
    "init",
    "Scaffold a new modular monolith solution.\n" +
    "Example: modulus init EShop --aspire --transport rabbitmq");
```

```csharp
// Option descriptions must show the valid values inline
var transportOption = new Option<string>(
    "--transport",
    () => "inmemory",
    "Message transport (inmemory | rabbitmq | azureservicebus). Default: inmemory");
```

---

## README Conversion Copy

The README Quick Start section is the single highest-impact copy surface. It drives trial. Three rules:

1. **First command must install the tool** — developers can't try what they can't install
2. **Second command must show the simplest success case** — not the most powerful case
3. **Show expected output** — removes uncertainty about whether it worked

```markdown
### Install the CLI

```bash
dotnet tool install --global ModulusKit.Cli
```

### Create a solution

```bash
modulus init EShop --aspire
# ✓ EShop created.
#   cd EShop && modulus add-module Catalog
```

### Add a module

```bash
cd EShop
modulus add-module Catalog
# ✓ Catalog module added.
```
```

---

## Anti-Patterns

### WARNING: Feature-List Copy in NuGet Descriptions

**The Problem:**

```xml
<!-- BAD — lists features, answers nothing the developer cares about -->
<Description>
  Includes ValidationBehavior, LoggingBehavior, UnhandledExceptionBehavior,
  MetricsBehavior, Result pattern, implicit conversions, Error types,
  FluentValidation integration, pipeline registration extensions...
</Description>
```

**Why This Breaks:** Developers scan NuGet looking for a solution to a problem, not a catalog of features. Feature lists don't answer "should I use this instead of what I have now?"

**The Fix:** Lead with the outcome ("scaffold in seconds"), follow with the differentiator ("no MediatR dependency").

### WARNING: Generic CLI Error Messages

**The Problem:**

```csharp
// BAD — developer has no idea what to do next
_console.WriteError("Error: invalid argument.");
return 1;
```

**Why This Breaks:** Generic errors create a dead-end. The developer either files a bug, abandons, or starts guessing — all of which delay or prevent activation.

**The Fix:** Every `WriteError` call must include what was wrong AND a corrected command:

```csharp
// GOOD — shows the problem and the fix in one message
_console.WriteError(
    $"Transport '{transport}' is not supported. " +
    "Use: --transport inmemory | rabbitmq | azureservicebus");
```

### WARNING: README Missing Expected Output

If the README shows `modulus init EShop` but doesn't show what success looks like, developers in CI/CD pipelines or remote terminals can't verify the command worked.

Always include a comment block showing expected output for each command in Quick Start.
