# Analyzer Configuration

## Adjusting Severities

Override the default severity of any rule using `.editorconfig`:

```ini
[*.cs]
# Make boundary violations an error (default)
dotnet_diagnostic.MOD001.severity = error

# Treat handler return type as an error in strict mode
dotnet_diagnostic.MOD002.severity = error

# Disable public setter info messages
dotnet_diagnostic.MOD005.severity = none
```

Available severity levels:

| Level | Behavior |
|---|---|
| `error` | Build fails, red squiggle in IDE |
| `warning` | Build succeeds, yellow squiggle in IDE |
| `suggestion` | Green dots in IDE, no build impact |
| `silent` | Not visible in IDE, still available in tooling |
| `none` | Completely disabled |

## Suppressing Individual Occurrences

When you have a valid reason to suppress a specific diagnostic, use `#pragma`:

```csharp
#pragma warning disable MOD001 // Cross-module reference needed for shared test utilities
using EShop.Modules.Catalog.Domain.Products;
#pragma warning restore MOD001
```

Or use the `[SuppressMessage]` attribute:

```csharp
using System.Diagnostics.CodeAnalysis;

[SuppressMessage("Modulus", "MOD005", Justification = "DTO, not a domain entity")]
public class ProductDto
{
    public string Name { get; set; }
}
```

## Enforcing in CI

### Treat warnings as errors

Add `-warnaserror` to your CI build to prevent any analyzer warning from passing:

```bash
dotnet build -warnaserror
```

### Selective enforcement

Promote specific rules to errors in `.editorconfig` for stricter enforcement:

```ini
[*.cs]
dotnet_diagnostic.MOD001.severity = error
dotnet_diagnostic.MOD002.severity = error
dotnet_diagnostic.MOD003.severity = error
dotnet_diagnostic.MOD004.severity = warning
dotnet_diagnostic.MOD005.severity = suggestion
```

### MSBuild property

You can also configure `TreatWarningsAsErrors` in `Directory.Build.props` for CI builds:

```xml
<PropertyGroup Condition="'$(CI)' == 'true'">
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

## Recommended Configuration

For most teams, the default severities work well:

| Rule | Default | Recommended |
|---|---|---|
| MOD001 | Error | Error -- boundary violations should always block |
| MOD002 | Warning | Warning or Error -- depends on how strictly you enforce the Result pattern |
| MOD003 | Warning | Warning -- gentle nudge toward Result pattern |
| MOD004 | Warning | Warning -- catches accidental infrastructure leaks |
| MOD005 | Info | Info or Suggestion -- useful but not critical |

## See Also

- [Rule Reference](./rules) -- Full documentation for each analyzer rule
- [Architecture Tests](/testing/architecture-tests) -- Complement analyzers with CI-level architecture enforcement
