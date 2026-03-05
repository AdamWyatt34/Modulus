# Analyzers

Modulus ships with five Roslyn analyzers that enforce modular architecture conventions directly in your IDE. Violations appear as real-time diagnostics (red squiggles, warnings, and suggestions) while you code, catching architectural issues before they reach a pull request.

## Why Analyzers?

Architecture tests with NetArchTest are powerful but only run during `dotnet test`. A developer can write violating code, commit it, push it, and only learn about the issue when CI fails. Roslyn analyzers close this gap by providing **instant feedback** in the editor.

Together, analyzers and architecture tests form two complementary layers:

| Layer | When | Feedback | Scope |
|---|---|---|---|
| **Roslyn Analyzers** | Real-time in IDE, `dotnet build` | Instant squiggles | Per-file analysis |
| **Architecture Tests** | `dotnet test`, CI pipeline | Test failure | Full assembly analysis |

## Rule Summary

| Rule | Severity | Description | Code Fix |
|------|----------|-------------|----------|
| [MOD001](./rules#mod001) | Error | Module boundary violation -- cross-module reference to non-Integration project | -- |
| [MOD002](./rules#mod002) | Warning | Handler not returning `Result` or `Result<T>` | -- |
| [MOD003](./rules#mod003) | Warning | Throwing exceptions for expected errors in handlers | Yes |
| [MOD004](./rules#mod004) | Warning | Infrastructure attributes (EF, JSON) in Domain layer | Yes |
| [MOD005](./rules#mod005) | Info | Public setter on entity property | Yes |

## Installation

If you scaffolded your solution with the Modulus CLI, analyzers are already configured. To add them manually:

```xml
<PackageReference Include="ModulusKit.Analyzers"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Both `ModulusKit.Analyzers` and `ModulusKit.Generators` are transitively included through `ModulusKit.Mediator.Abstractions`.

## What's Next

- **[Rule Reference](./rules)** -- Detailed documentation for each rule with violation and correct code examples
- **[Configuration](./configuration)** -- Adjust severities, suppress rules, and enforce in CI
