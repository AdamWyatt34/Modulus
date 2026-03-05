# ModulusKit.Analyzers

Roslyn analyzers and code fixes for enforcing Modulus modular architecture conventions directly in your IDE.

## Installation

```bash
dotnet add package ModulusKit.Analyzers
```

Or as an analyzer reference in your `.csproj`:

```xml
<PackageReference Include="ModulusKit.Analyzers"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

> **Note:** This package is transitively included through `ModulusKit.Mediator.Abstractions`. If you already reference that package, no additional setup is needed.

## Rules

| Rule | Severity | Description | Code Fix |
|------|----------|-------------|----------|
| MOD001 | Error | Module boundary violation -- cross-module reference to non-Integration project | -- |
| MOD002 | Warning | Handler not returning `Result` or `Result<T>` | -- |
| MOD003 | Warning | Throwing exceptions for expected errors in handlers instead of returning `Error` | Yes |
| MOD004 | Warning | Infrastructure attributes (EF, JSON) in Domain layer | Yes |
| MOD005 | Info | Public setter on entity property | Yes |

## Configuration

Adjust severities in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.MOD001.severity = error
dotnet_diagnostic.MOD002.severity = warning
dotnet_diagnostic.MOD003.severity = warning
dotnet_diagnostic.MOD004.severity = warning
dotnet_diagnostic.MOD005.severity = suggestion
```

Suppress individual occurrences with `#pragma`:

```csharp
#pragma warning disable MOD001
using EShop.Modules.Catalog.Domain.Products;
#pragma warning restore MOD001
```

## Learn More

See the [Modulus documentation](https://adamwyatt34.github.io/Modulus/analyzers/) for full rule reference with examples.
