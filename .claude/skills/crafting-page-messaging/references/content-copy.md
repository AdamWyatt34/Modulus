# Content Copy Reference

## Contents
- README Copy Principles
- Package Description Formulas
- CLI Help Text Formulas
- Console Message Tone
- Workflow: Updating Copy Across All Surfaces

---

## README Copy Principles

The `README.md` at the project root serves as the NuGet readme for all packages via `<PackageReadmeFile>`.
It is the primary persuasion surface. Developers arrive from NuGet search, GitHub search, or a blog post
and need to immediately understand:

1. What problem it solves
2. Whether it fits their stack
3. How to get started in under 5 minutes

Structure the README in this order:
1. **One-line pitch** — the value prop in plain language
2. **Install command** — `dotnet add package ModulusKit.Mediator`
3. **Minimal working example** — copy-pasteable, compiles immediately
4. **Feature list** — bullet points, not prose
5. **Package breakdown** — which packages to install for each use case

AVOID: architecture essays at the top, badges before the pitch, feature comparison tables as the first section.

---

## Package Description Formulas

NuGet `<Description>` is limited to ~1000 chars but should stay under 200. Use one of these formulas:

### Formula 1: Adjective + Noun + Feature List
```
[Adjective] [noun] for .NET with [feature], [feature], and [feature].
```

```xml
<!-- Current — good example of this formula -->
<Description>Lightweight CQRS mediator for .NET with pipeline behaviors, validation, logging, and a built-in Result pattern.</Description>
```

### Formula 2: Problem → Solution
```
[What the developer is building] powered by [mechanism]. [Key differentiator].
```

```xml
<!-- Example for Modulus.Generators -->
<Description>Source-generator-powered handler auto-registration for .NET modular monoliths. Zero reflection, compile-time safety.</Description>
```

### Formula 3: Tool + Action + Outcome
```
[Tool type] for scaffolding [what] with [built-in features].
```

```xml
<!-- Current CLI description — good example -->
<Description>CLI tool for scaffolding .NET modular monolith solutions with built-in CQRS mediator, messaging, and Aspire support.</Description>
```

---

## CLI Help Text Formulas

### Command Description: Verb + Object + Context
```
[Verb] a [object] [context]
```

```csharp
// src/Modulus.Cli/Commands/InitCommand.cs
new Command("init", "Scaffold a new modular monolith solution")

// src/Modulus.Cli/Commands/AddModuleCommand.cs
new Command("add-module", "Add a new module to an existing Modulus solution")
```

### Argument Description: Format + Purpose
```
[Format constraint] [purpose description]
```

```csharp
new Argument<string>("solution-name")
{
    Description = "PascalCase name of the solution to create",
}

new Argument<string>("module-name")
{
    Description = "PascalCase name of the module to add",
}
```

### Option Description: Outcome sentence or enumerated values

```csharp
// Boolean flags: describe what is included/enabled
new Option<bool>("--aspire")
{
    Description = "Include .NET Aspire AppHost and ServiceDefaults projects",
}

// String options with limited values: list all values in parens
new Option<string>("--transport")
{
    Description = "Messaging transport to pre-configure (inmemory, rabbitmq, azureservicebus)",
}

// Path options: describe what it overrides
new Option<string?>("--output")
{
    Description = "Output directory (default: current directory)",
}
```

---

## Console Message Tone

`IConsoleOutput` has three channels — use each consistently:

| Channel | When to Use | Tone |
|---------|-------------|------|
| `WriteSuccess` | Operation completed fully | Confirming, specific |
| `WriteLine` | Progress or summary details | Neutral, factual |
| `WriteError` | Failure or warning | Direct, includes fix |

### Tone DO/DON'T

```csharp
// DO: name the artifact, give the location
console.WriteSuccess($"Solution '{solutionName}' created successfully at {solutionRoot}");

// DON'T: generic confirmation
console.WriteSuccess("Done!");

// DO: prefix warnings as warnings so CI doesn't treat them as hard failures
console.WriteError($"Warning: dotnet restore failed with exit code {restoreResult}. You may need to run it manually.");

// DON'T: use WriteError for warnings that don't block the operation without prefixing "Warning:"
console.WriteError($"dotnet restore failed.");
```

---

## Workflow: Updating Copy Across All Surfaces

Copy this checklist when making a messaging pass:

- [ ] Step 1: Read all `<Description>` fields — `grep -r "<Description>" src/ --include="*.csproj"`
- [ ] Step 2: Read all `new Command(` and `new Argument<` descriptions — `grep -r "Description = " src/Modulus.Cli/Commands/`
- [ ] Step 3: Read all `console.Write*` calls — `grep -r "console.Write" src/Modulus.Cli/Handlers/`
- [ ] Step 4: Read README introduction section
- [ ] Step 5: Apply changes, verify descriptions match their actual behavior (a renamed option's description must also update)
- [ ] Step 6: Build and run `modulus --help` to verify CLI output renders correctly: `dotnet run --project src/Modulus.Cli/Modulus.Cli.csproj -- --help`
