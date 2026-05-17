# Distribution Reference

## Contents
- NuGet Publishing Pipeline
- Package Metadata Completeness
- Versioning Strategy
- GitHub Release Workflow
- Anti-Patterns in Package Distribution

Distribution for ModulusKit means NuGet.org is always current, package metadata is complete, and version bumps are coordinated across all 7 packages simultaneously via central versioning.

---

## NuGet Publishing Pipeline

The CI workflow in `.github/workflows/` handles packing and publishing. Packages are built from the solution root using central version management.

```powershell
# Local validation before pushing a release tag
dotnet build Modulus.slnx --configuration Release
dotnet test Modulus.slnx --configuration Release
dotnet pack Modulus.slnx --configuration Release --output ./nupkgs

# Inspect what got packed
Get-ChildItem ./nupkgs/*.nupkg | Select-Object Name, Length
```

```powershell
# Test install from local feed before publishing to NuGet
dotnet nuget add source ./nupkgs --name local-modulus
dotnet tool install --global ModulusKit.Cli --add-source ./nupkgs
modulus --version
dotnet nuget remove source local-modulus
```

---

## Package Metadata Completeness

Every `.csproj` must have this metadata block. Missing fields cause NuGet search ranking penalties.

```xml
<!-- Required in every packable project's .csproj -->
<PropertyGroup>
  <PackageId>ModulusKit.[Name]</PackageId>
  <Description>[Concrete value statement ≤200 chars]</Description>
  <PackageTags>dotnet;csharp;modular-monolith;cqrs;mediator;[specific tags]</PackageTags>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageProjectUrl>https://github.com/adamwyatt34/Modulus</PackageProjectUrl>
  <RepositoryUrl>https://github.com/adamwyatt34/Modulus</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>

<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="/" />
</ItemGroup>
```

Note: `<Version>` is NOT set here — it comes from `Directory.Build.props`.

---

## Versioning Strategy

All 7 packages ship at the same version. NEVER release packages at different versions — consumers who upgrade one package expect all packages to be compatible at the same version.

```xml
<!-- Directory.Build.props — single source of truth -->
<PropertyGroup>
  <Version>1.2.0</Version>
  <AssemblyVersion>1.2.0</AssemblyVersion>
  <FileVersion>1.2.0</FileVersion>
</PropertyGroup>
```

Version bumping workflow:
1. Update `Version` in `Directory.Build.props`
2. Update `CHANGELOG.md` with release notes
3. Commit: `git commit -m "chore: bump version to 1.2.0"`
4. Tag: `git tag v1.2.0`
5. Push tag — CI publishes to NuGet

---

## WARNING: Per-Project Version Overrides

**The Problem:**
```xml
<!-- BAD — version specified in individual .csproj -->
<PackageReference Include="ModulusKit.Mediator.Abstractions" Version="1.1.0" />
```

**Why This Breaks:**
1. `ModulusKit.Mediator` at v1.2.0 may depend on `ModulusKit.Mediator.Abstractions` at v1.2.0 — version mismatch causes runtime failures
2. Breaks central package management — `Directory.Packages.props` becomes unreliable
3. NuGet restore produces non-deterministic results across machines

**The Fix:**
```xml
<!-- Directory.Packages.props — all versions here -->
<PackageVersion Include="ModulusKit.Mediator.Abstractions" Version="1.2.0" />
<PackageVersion Include="ModulusKit.Mediator" Version="1.2.0" />

<!-- Individual .csproj — no Version attribute -->
<PackageReference Include="ModulusKit.Mediator.Abstractions" />
```

---

## GitHub Release Workflow

```yaml
# .github/workflows/publish.yml (simplified structure)
on:
  push:
    tags:
      - 'v*'

jobs:
  publish:
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet build Modulus.slnx --configuration Release
      - run: dotnet test Modulus.slnx --configuration Release
      - run: dotnet pack Modulus.slnx --configuration Release --output ./nupkgs
      - run: dotnet nuget push ./nupkgs/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
```

---

## Distribution Checklist

Copy and track before each release:
- [ ] `Directory.Build.props` version updated
- [ ] `CHANGELOG.md` entry written with breaking changes, features, fixes
- [ ] All packages build without warnings: `dotnet build Modulus.slnx --configuration Release`
- [ ] All tests pass: `dotnet test Modulus.slnx --configuration Release`
- [ ] Local pack validation: `dotnet pack Modulus.slnx --output ./nupkgs`
- [ ] Each `.nupkg` has README embedded (inspect with NuGet Package Explorer)
- [ ] Symbol packages (`.snupkg`) generated alongside `.nupkg`
- [ ] Git tag pushed to trigger CI publish
- [ ] NuGet.org package pages updated (allow 10-15 min for indexing)
