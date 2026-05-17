# Programmatic Reference — Structured Signals

## Contents
- CI Metadata Injection
- Version Badge Automation
- Metadata Audit Script
- Pre-Release Checklist
- Anti-Patterns

---

## CI Metadata Injection

The GitHub Actions CI workflow should inject version metadata from git tags into the pack step.
This ensures NuGet packages always carry correct version and commit information.

```yaml
# .github/workflows/ci.yml
- name: Pack NuGet packages
  run: |
    dotnet pack Modulus.slnx `
      --configuration Release `
      --output ./nupkgs `
      -p:PackageVersion=${{ github.ref_name }} `
      -p:RepositoryCommit=${{ github.sha }}
```

In `Directory.Build.props`, accept these as overridable properties:

```xml
<PropertyGroup>
  <!-- Defaults for local dev; CI overrides via -p: flags -->
  <PackageVersion Condition="'$(PackageVersion)' == ''">0.0.1-dev</PackageVersion>
  <RepositoryCommit Condition="'$(RepositoryCommit)' == ''">local</RepositoryCommit>
</PropertyGroup>
```

## Version Badge Automation

GitHub Actions can update the README badge URLs automatically on release. Alternatively, use
shields.io dynamic badges that pull from NuGet.org without any build step:

```markdown
<!-- Static badge for each package — update version manually on release -->
[![NuGet](https://img.shields.io/nuget/v/ModulusKit.Mediator?label=ModulusKit.Mediator)](https://www.nuget.org/packages/ModulusKit.Mediator)
[![NuGet](https://img.shields.io/nuget/v/ModulusKit.Messaging?label=ModulusKit.Messaging)](https://www.nuget.org/packages/ModulusKit.Messaging)
[![NuGet](https://img.shields.io/nuget/v/ModulusKit.Generators?label=ModulusKit.Generators)](https://www.nuget.org/packages/ModulusKit.Generators)
[![NuGet](https://img.shields.io/nuget/v/ModulusKit.Analyzers?label=ModulusKit.Analyzers)](https://www.nuget.org/packages/ModulusKit.Analyzers)
[![NuGet](https://img.shields.io/nuget/v/ModulusKit.Cli?label=ModulusKit.Cli)](https://www.nuget.org/packages/ModulusKit.Cli)
```

shields.io fetches live from NuGet.org — the badge always shows the latest published version
without any CI update step.

## Metadata Audit Script

Run before every release to catch missing metadata across all packages:

```powershell
# scripts/audit-metadata.ps1
$projects = Get-ChildItem -Path "src" -Filter "*.csproj" -Recurse

$required = @("PackageId", "Description", "PackageTags", "PackageReadmeFile",
              "PackageLicenseExpression", "PackageProjectUrl", "RepositoryUrl")

foreach ($project in $projects) {
    [xml]$csproj = Get-Content $project.FullName
    $props = $csproj.Project.PropertyGroup

    $missing = @()
    foreach ($field in $required) {
        $value = $props.$field
        if ([string]::IsNullOrWhiteSpace($value)) {
            $missing += $field
        }
    }

    if ($missing.Count -gt 0) {
        Write-Warning "$($project.Name) missing: $($missing -join ', ')"
    } else {
        Write-Host "$($project.Name): OK" -ForegroundColor Green
    }
}
```

```powershell
# Run it
pwsh scripts/audit-metadata.ps1
```

## Pre-Release Checklist

Copy this checklist and track progress before publishing a new version:

- [ ] Step 1: Run `pwsh scripts/audit-metadata.ps1` — all packages report OK
- [ ] Step 2: Verify `PackageVersion` in `Directory.Build.props` matches the intended release tag
- [ ] Step 3: Confirm `PackageReadmeFile` exists and renders correctly (open locally)
- [ ] Step 4: Build with `dotnet pack --configuration Release --output ./nupkgs`
- [ ] Step 5: Inspect generated `.nupkg` with NuGet Package Explorer or `dotnet tool run nuget-inspector`
- [ ] Step 6: Run `dotnet test Modulus.slnx` — all tests pass
- [ ] Step 7: Push git tag — CI workflow triggers NuGet push

## XML Doc Coverage Check

Add a CI step to fail if public APIs are missing XML docs:

```xml
<!-- Directory.Build.props -->
<PropertyGroup Condition="'$(CI)' == 'true'">
  <!-- Treat missing XML doc as an error in CI -->
  <NoWarn>$(NoWarn)</NoWarn>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <WarningsAsErrors>CS1591</WarningsAsErrors>
</PropertyGroup>
```

Suppress on internal/generated types explicitly:

```csharp
// Generated file — suppress doc requirement
#pragma warning disable CS1591
[GeneratedCode("Modulus.Generators", "1.0.0")]
internal static class GeneratedHandlerRegistrations { ... }
#pragma warning restore CS1591
```

---

## WARNING: Hardcoded Version in Directory.Build.props for CI

Never commit a specific release version number to `Directory.Build.props`. CI should inject it
via `-p:PackageVersion`. A hardcoded version means every PR triggers the same version, making
NuGet pushes conflict.

```xml
<!-- BAD — hardcoded version -->
<PackageVersion>1.2.0</PackageVersion>

<!-- GOOD — CI injects, local dev gets a safe default -->
<PackageVersion Condition="'$(PackageVersion)' == ''">0.0.1-dev</PackageVersion>
```

## WARNING: No Deterministic Build

Without deterministic builds, two builds from the same source produce different binaries, which
breaks NuGet package verification. Enable in `Directory.Build.props`:

```xml
<Deterministic>true</Deterministic>
<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
```
