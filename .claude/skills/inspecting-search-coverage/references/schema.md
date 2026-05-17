# Schema Reference — NuGet Package Completeness

## Contents
- Full `.csproj` metadata schema
- Directory.Build.props shared fields
- Per-package required vs. recommended fields
- Current state per package

## Full Metadata Schema

A fully instrumented packable `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Identity -->
    <PackageId>ModulusKit.Mediator</PackageId>
    <Description>Lightweight CQRS mediator for .NET 10 with pipeline behaviors, Result pattern, logging, and FluentValidation integration. No MediatR dependency.</Description>
    <PackageTags>mediator;cqrs;pipeline;result-pattern;modular-monolith;no-mediatR;dotnet;handler</PackageTags>

    <!-- Presentation -->
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageIcon>icon.png</PackageIcon>

    <!-- Legal -->
    <!-- PackageLicenseExpression and Authors come from Directory.Build.props -->

    <!-- Links -->
    <!-- PackageProjectUrl, RepositoryUrl come from Directory.Build.props -->

    <!-- Behavior -->
    <PackageRequireLicenseAcceptance>false</PackageRequireLicenseAcceptance>
  </PropertyGroup>

  <ItemGroup>
    <!-- README must be explicitly included as content -->
    <None Include="README.md" Pack="true" PackagePath="\" />
    <!-- Icon must also be explicitly included -->
    <None Include="..\..\assets\icon.png" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

## Directory.Build.props Shared Fields

```xml
<!-- Directory.Build.props — all shared metadata in one place -->
<PropertyGroup>
  <Version>1.0.1</Version>
  <Authors>Adam Wyatt</Authors>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageProjectUrl>https://github.com/adamwyatt34/Modulus</PackageProjectUrl>
  <RepositoryUrl>https://github.com/adamwyatt34/Modulus</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <!-- Add: -->
  <Copyright>Copyright © Adam Wyatt</Copyright>
  <PackageRequireLicenseAcceptance>false</PackageRequireLicenseAcceptance>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
</PropertyGroup>
```

`IncludeSymbols` + `SymbolPackageFormat=snupkg` enables source stepping in debuggers — a significant quality signal for library packages.

## Current State Per Package

| Package | Description | Tags | Readme | License | ProjectUrl | Icon |
|---------|-------------|------|--------|---------|------------|------|
| `ModulusKit.Mediator` | ✅ | ⚠️ sparse | ✅ | via props | via props | ❌ |
| `ModulusKit.Mediator.Abstractions` | ⚠️ weak | ⚠️ sparse | ✅ | via props | via props | ❌ |
| `ModulusKit.Messaging` | ? | ? | ? | via props | via props | ❌ |
| `ModulusKit.Messaging.Abstractions` | ? | ? | ? | via props | via props | ❌ |
| `ModulusKit.Generators` | ❌ stale | ⚠️ sparse | ❌ | via props | via props | ❌ |
| `ModulusKit.Analyzers` | ❌ thin | ⚠️ sparse | ❌ | ❌ missing | ❌ missing | ❌ |
| `ModulusKit.Cli` | ✅ | ✅ | ✅ (root) | via props | via props | ❌ |

Legend: ✅ Good · ⚠️ Needs improvement · ❌ Missing

## Analyzer/Generator Special Cases

Roslyn components target `netstandard2.0` and have `IsRoslynComponent=true`. This does NOT exempt them from metadata requirements. They still appear on NuGet.org and need:

```xml
<!-- Special: Analyzers need DevelopmentDependency = false to show on listing -->
<!-- (DevelopmentDependency=true hides the package from transitive consumers) -->
<!-- ModulusKit.Generators correctly has DevelopmentDependency=true — this is right for generators -->
<!-- ModulusKit.Analyzers should NOT have DevelopmentDependency=true — analyzers are visible -->
```

## Symbol Package Support

```xml
<!-- Directory.Build.props — enables .snupkg for source debugging -->
<PropertyGroup>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>
```

Pack command then produces both `.nupkg` and `.snupkg`. Push both:

```powershell
dotnet nuget push ./nupkgs/*.nupkg --source "https://api.nuget.org/v3/index.json" --api-key $env:NUGET_API_KEY
dotnet nuget push ./nupkgs/*.snupkg --source "https://api.nuget.org/v3/index.json" --api-key $env:NUGET_API_KEY
```

See the **adding-structured-signals** skill for additional NuGet metadata signals.
