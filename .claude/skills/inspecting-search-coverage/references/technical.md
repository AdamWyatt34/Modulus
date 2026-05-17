# Technical Reference — NuGet Package Metadata

## Contents
- Required metadata fields
- Shared vs. per-package metadata
- Anti-patterns in current `.csproj` files
- Validation workflow

## Required Metadata Fields Per Package

Every packable `.csproj` in `src/` must have all of these:

```xml
<!-- src/Modulus.Mediator/Modulus.Mediator.csproj — GOOD example -->
<PropertyGroup>
  <PackageId>ModulusKit.Mediator</PackageId>
  <Description>Lightweight CQRS mediator for .NET with pipeline behaviors, validation, logging, and a built-in Result pattern.</Description>
  <PackageTags>mediator;cqrs;pipeline;validation;result-pattern</PackageTags>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```

## Shared Metadata in Directory.Build.props

Fields that apply to ALL packages belong in `Directory.Build.props`, not repeated per `.csproj`:

```xml
<!-- Directory.Build.props — extend this -->
<PropertyGroup>
  <Version>1.0.1</Version>
  <Authors>Adam Wyatt</Authors>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageProjectUrl>https://github.com/adamwyatt34/Modulus</PackageProjectUrl>
  <RepositoryUrl>https://github.com/adamwyatt34/Modulus</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <!-- Add these missing fields: -->
  <Copyright>Copyright © Adam Wyatt</Copyright>
  <PackageIcon>icon.png</PackageIcon>
  <PackageRequireLicenseAcceptance>false</PackageRequireLicenseAcceptance>
</PropertyGroup>
```

## WARNING: Analyzer/Generator Packages Missing Core Metadata

**The Problem:**

```xml
<!-- src/Modulus.Analyzers/Modulus.Analyzers.csproj — BAD -->
<PropertyGroup>
  <PackageId>ModulusKit.Analyzers</PackageId>
  <Description>Roslyn analyzers and code fixes for Modulus conventions.</Description>
  <PackageTags>analyzer;roslyn;code-fix</PackageTags>
  <!-- No PackageReadmeFile, PackageLicenseExpression, or PackageProjectUrl -->
</PropertyGroup>
```

**Why This Breaks:**
1. NuGet.org listing shows no README tab — developers can't understand the package without visiting GitHub
2. Missing license expression triggers a warning in `dotnet pack` output
3. Missing `<PackageProjectUrl>` means NuGet.org can't link to the source

**The Fix:**

```xml
<!-- src/Modulus.Analyzers/Modulus.Analyzers.csproj — GOOD -->
<PropertyGroup>
  <PackageId>ModulusKit.Analyzers</PackageId>
  <Description>Roslyn compile-time analyzers enforcing modular architecture conventions (MOD001–MOD005). Catches cross-module references, Result pattern violations, thrown exceptions, and infrastructure attribute misuse at build time.</Description>
  <PackageTags>analyzer;roslyn;code-fix;modular-monolith;architecture;cqrs</PackageTags>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
<ItemGroup>
  <None Include="..\..\README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

## WARNING: Stale Generator Description

**The Problem:**

```xml
<!-- src/Modulus.Generators/Modulus.Generators.csproj — BAD -->
<Description>Incremental source generators for Modulus, including StronglyTypedId.</Description>
```

**Why This Breaks:**
- "StronglyTypedId" is not what this generator does — it generates `AddModulusHandlers()` and `AddAllModules()`
- Developers searching for the actual capability won't find it
- Creates confusion about what the package actually provides

**The Fix:**

```xml
<Description>Incremental Roslyn source generators for ModulusKit. Auto-generates AddModulusHandlers() to register all CQRS handlers, validators, and event handlers at compile time — no manual DI registration required.</Description>
<PackageTags>source-generator;roslyn;cqrs;dependency-injection;auto-registration;modular-monolith</PackageTags>
```

## Validation Workflow

1. Edit `.csproj` metadata
2. Run: `dotnet pack --configuration Release --output ./nupkgs`
3. Open `.nupkg` as zip and inspect `/[id].nuspec` for rendered values
4. If NuGet.org listing: wait for indexing (typically <1 minute after upload)
5. Search NuGet.org for your target keywords — verify package appears in results

## Field Reference

| Field | Required | Where | Notes |
|-------|----------|-------|-------|
| `PackageId` | Yes | per `.csproj` | Must be `ModulusKit.*` |
| `Description` | Yes | per `.csproj` | 150–250 chars optimal for NuGet search |
| `PackageTags` | Yes | per `.csproj` | Semicolon-separated, lowercase |
| `PackageReadmeFile` | Yes | per `.csproj` | Requires matching `<None Include=.../>` |
| `Version` | Yes | `Directory.Build.props` | Single source of truth |
| `Authors` | Yes | `Directory.Build.props` | Shared |
| `Copyright` | Recommended | `Directory.Build.props` | Missing currently |
| `PackageIcon` | Recommended | `Directory.Build.props` | Improves listing visibility |
| `PackageLicenseExpression` | Yes | `Directory.Build.props` | Missing from analyzer/generator |
