# Conversion Optimization Reference

## Contents
- NuGet Package Description Optimization
- CLI Help Text Optimization
- Console Message Optimization
- Anti-Patterns

---

## NuGet Package Description Optimization

NuGet.org displays the `<Description>` field prominently on the package page and in search results.
Developers scan these in 2–3 seconds. The description must answer: "Does this solve my exact problem?"

### Current Descriptions (Baseline)

```xml
<!-- src/Modulus.Cli/Modulus.Cli.csproj -->
<Description>CLI tool for scaffolding .NET modular monolith solutions with built-in CQRS mediator, messaging, and Aspire support.</Description>

<!-- src/Modulus.Mediator/Modulus.Mediator.csproj -->
<Description>Lightweight CQRS mediator for .NET with pipeline behaviors, validation, logging, and a built-in Result pattern.</Description>
```

These are solid. Lead with the adjective (lightweight, built-in) and name concrete features.

### Tags Are Search Terms

```xml
<!-- Good — specific, searched terms -->
<PackageTags>mediator;cqrs;pipeline;validation;result-pattern</PackageTags>

<!-- Bad — generic, not searched -->
<PackageTags>dotnet;library;patterns</PackageTags>
```

Tags map directly to NuGet search filters. Add terms developers use when looking for alternatives:
`no-mediatR`, `custom-mediator`, `source-generator` are findable; `utilities` is not.

### Shared Metadata in Directory.Build.props

```xml
<!-- Directory.Build.props -->
<Authors>Adam Wyatt</Authors>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<PackageProjectUrl>https://github.com/adamwyatt34/Modulus</PackageProjectUrl>
```

`PackageProjectUrl` appears as the "Project Site" link on NuGet. Keep it pointing to the GitHub README,
not a wiki or sub-page — the README IS the landing page.

---

## CLI Help Text Optimization

Every argument and option description renders verbatim in `modulus --help`. Developers read `--help`
when stuck — the description is the last line of support before they abandon the command.

### Arguments: Describe the Expected Format

```csharp
// GOOD — tells developer exactly what format is expected
var solutionNameArg = new Argument<string>("solution-name")
{
    Description = "PascalCase name of the solution to create",
};

// BAD — no format guidance
var solutionNameArg = new Argument<string>("solution-name")
{
    Description = "Name of the solution",
};
```

### Options: Describe the Outcome, Not the Mechanism

```csharp
// GOOD — describes what happens when set
var aspireOption = new Option<bool>("--aspire")
{
    Description = "Include .NET Aspire AppHost and ServiceDefaults projects",
};

// BAD — describes the flag semantically (developer already knows it "enables" something)
var aspireOption = new Option<bool>("--aspire")
{
    Description = "Enable Aspire support",
};
```

### Enum-Like Options: List Valid Values in Description

```csharp
// GOOD — valid values are discoverable without running the command
var transportOption = new Option<string>("--transport")
{
    Description = "Messaging transport to pre-configure (inmemory, rabbitmq, azureservicebus)",
    DefaultValueFactory = _ => "inmemory",
};
```

Including valid values in the description prevents the most common error message:
`Invalid transport 'X'. Valid values: inmemory, rabbitmq, azureservicebus.`

---

## Console Message Optimization

`IConsoleOutput` has three levels: `WriteLine`, `WriteError`, `WriteSuccess`. Use them semantically —
mixing them breaks terminal tooling that pipes stdout and filters stderr.

### Success Messages: Confirm + Summarize

```csharp
// src/Modulus.Cli/Handlers/InitHandler.cs
console.WriteSuccess($"Solution '{solutionName}' created successfully at {solutionRoot}");
console.WriteLine($"  Aspire: {(includeAspire ? "Yes" : "No")}");
console.WriteLine($"  Transport: {transport}");
console.WriteLine($"  Git: {(noGit ? "Skipped" : "Initialized")}");
```

The summary block answers "what did defaults produce?" without requiring the developer to re-read the command.

### Error Messages: Problem + Fix in One Line

```csharp
// GOOD — problem is stated AND fix is given
console.WriteError($"'{solutionName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");

// BAD — problem only
console.WriteError($"Invalid solution name.");
```

---

## Anti-Patterns

### WARNING: Generic NuGet Descriptions

**The Problem:**
```xml
<!-- BAD - could describe any library -->
<Description>A .NET library for building better applications.</Description>
```

**Why This Breaks:**
1. Fails NuGet search relevance — no keywords match developer queries
2. Gives no signal on whether to investigate further
3. Forces the developer to click through to README before deciding

**The Fix:**
```xml
<!-- GOOD - specific, feature-named, adjective-first -->
<Description>Source-generator-powered handler auto-registration for .NET modular monoliths. No reflection, no manual DI setup.</Description>
```

### WARNING: Vague Error Messages

**The Problem:**
```csharp
// BAD - developer must re-read docs to recover
console.WriteError("Invalid input.");
```

**Why This Breaks:**
1. Forces the developer to re-run `--help` or check docs
2. Breaks CI pipelines where no human reads the output
3. Erodes trust in the tool

**The Fix:**
```csharp
// GOOD - echo the bad value, list valid alternatives
console.WriteError($"Invalid transport '{transport}'. Valid values: inmemory, rabbitmq, azureservicebus.");
```
