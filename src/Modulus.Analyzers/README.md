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

> **Note:** This is a development-dependency (analyzer) package -- it does **not** flow transitively through other `ModulusKit.*` packages or through project references. Add the reference above to each project where the MOD rules should run. Solutions scaffolded by the `modulus` CLI have this wired up already.

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
using EShop.Catalog.Domain.Products;
#pragma warning restore MOD001
```

## Learn More

See the [Modulus documentation](https://adamwyatt34.github.io/Modulus/analyzers/) for full rule reference with examples.
