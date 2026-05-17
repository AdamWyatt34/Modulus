# Schema Reference — Structured Signals

## Contents
- NuGet Package Schema Fields
- Directory.Build.props Schema
- .csproj Pack Schema
- XML Documentation Schema
- Repository Metadata Schema

---

## NuGet Package Schema Fields

These are the structured fields NuGet.org indexes. Know which are required vs. optional.

| Field | .csproj Property | Required | Indexed for Search |
|-------|-----------------|----------|-------------------|
| Package ID | `<PackageId>` | Yes | Yes (highest weight) |
| Version | `<PackageVersion>` | Yes | No |
| Description | `<Description>` | Recommended | Yes |
| Tags | `<PackageTags>` | Recommended | Yes (tag filter) |
| README | `<PackageReadmeFile>` | No | No (rendered only) |
| Release notes | `<PackageReleaseNotes>` | No | No |
| Authors | `<Authors>` | Yes | No |
| License | `<PackageLicenseExpression>` | Recommended | No |
| Project URL | `<PackageProjectUrl>` | No | No |
| Repository URL | `<RepositoryUrl>` | No | Enables SourceLink |
| Icon | `<PackageIcon>` | No | Shown in listings |

## Directory.Build.props Schema

Complete reference for the shared properties file. All packages inherit these.

```xml
<Project>
  <PropertyGroup>
    <!-- Identity -->
    <Authors>Adam Wyatt</Authors>
    <Company>ModulusKit</Company>
    <Copyright>Copyright © 2024 Adam Wyatt</Copyright>

    <!-- Repository -->
    <RepositoryUrl>https://github.com/AdamWyatt34/Modulus</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageProjectUrl>https://github.com/AdamWyatt34/Modulus</PackageProjectUrl>

    <!-- License -->
    <PackageLicenseExpression>MIT</PackageLicenseExpression>

    <!-- Version — CI injects; local dev uses fallback -->
    <PackageVersion Condition="'$(PackageVersion)' == ''">0.0.1-dev</PackageVersion>

    <!-- Build quality -->
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>

    <!-- Symbols -->
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>

    <!-- Documentation -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>

    <!-- Target -->
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

## .csproj Pack Schema

Minimal per-package schema. Every packable project needs exactly these fields:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Required: unique NuGet ID -->
    <PackageId>ModulusKit.Mediator</PackageId>

    <!-- Required: first 160 chars appear in search results -->
    <Description>Custom CQRS mediator for .NET 10 modular monoliths. Pipeline behaviors, Result pattern, FluentValidation integration. No MediatR dependency.</Description>

    <!-- Recommended: semicolon-delimited, 8-10 terms max -->
    <PackageTags>cqrs;mediator;modular-monolith;dotnet;net10;moduluskit;result-pattern;pipeline;behaviors;clean-architecture</PackageTags>

    <!-- Required for NuGet.org listing page rendering -->
    <PackageReadmeFile>README.md</PackageReadmeFile>

    <!-- Optional: short notes embedded in .nupkg -->
    <PackageReleaseNotes>See https://github.com/AdamWyatt34/Modulus/releases for full changelog.</PackageReleaseNotes>
  </PropertyGroup>

  <!-- Wire the README file into the pack -->
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

## XML Documentation Schema

Triple-slash tags supported by Roslyn and rendered by IntelliSense:

```csharp
/// <summary>One-line description of the member.</summary>
/// <remarks>
/// Extended explanation. May include multi-line text, code blocks:
/// <code>
/// var result = await mediator.Send(command);
/// </code>
/// </remarks>
/// <typeparam name="T">Description of the type parameter.</typeparam>
/// <param name="value">Description of the parameter.</param>
/// <returns>What the method returns and when.</returns>
/// <exception cref="InvalidOperationException">When this is thrown (rare, document if unavoidable).</exception>
/// <example>
/// <code>
/// // Working usage example
/// </code>
/// </example>
/// <seealso cref="RelatedType"/>
/// <see cref="OtherType"/>
```

## Repository Metadata Schema

GitHub repository signals that affect discoverability in GitHub search and ecosystem lists:

```
Repository Description (max 350 chars):
  "Scaffold production-ready .NET 10 modular monoliths. Custom CQRS mediator, transactional outbox/inbox, Roslyn source generators, and CLI tool. No MediatR dependency."

Topics (max 20, use the most searched):
  dotnet, csharp, cqrs, mediator, modular-monolith, clean-architecture,
  source-generator, roslyn, masstransit, outbox-pattern, dotnet-tool,
  nuget, net10, result-pattern, fluent-validation

Website URL:
  https://www.nuget.org/packages?q=ModulusKit
```

Topics are the GitHub equivalent of NuGet PackageTags. They surface the repo in GitHub topic pages
(e.g., `github.com/topics/modular-monolith`) and in `awesome-dotnet` lists that scrape topics.
