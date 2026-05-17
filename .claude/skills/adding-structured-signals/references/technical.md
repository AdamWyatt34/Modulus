# Technical Reference — Structured Signals

## Contents
- Package Metadata Properties
- XML Documentation
- README Embedding in Packages
- SourceLink / Symbol Packages
- Anti-Patterns

---

## Package Metadata Properties

All metadata lives in `Directory.Build.props` (shared) or individual `.csproj` (package-specific).
Never duplicate in both — shared values go in `Directory.Build.props`.

```xml
<!-- Directory.Build.props — shared across all packages -->
<PropertyGroup>
  <Authors>Adam Wyatt</Authors>
  <Company>ModulusKit</Company>
  <Copyright>Copyright © 2024 Adam Wyatt</Copyright>
  <RepositoryUrl>https://github.com/AdamWyatt34/Modulus</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageProjectUrl>https://github.com/AdamWyatt34/Modulus</PackageProjectUrl>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

```xml
<!-- src/Modulus.Mediator/Modulus.Mediator.csproj — package-specific -->
<PropertyGroup>
  <PackageId>ModulusKit.Mediator</PackageId>
  <Description>Custom CQRS mediator for .NET modular monoliths. Pipeline behaviors, Result pattern, FluentValidation integration. No MediatR dependency.</Description>
  <PackageTags>cqrs;mediator;modular-monolith;dotnet;result-pattern;pipeline;behaviors;clean-architecture</PackageTags>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>

<ItemGroup>
  <None Include="../../README.md" Pack="true" PackagePath="/" />
</ItemGroup>
```

## XML Documentation

`GenerateDocumentationFile` in `Directory.Build.props` enables XML doc output. Every public type
and member on the public API surface needs triple-slash comments.

```csharp
// src/Modulus.Mediator.Abstractions/Messaging/IMediator.cs

/// <summary>
/// Sends a command or query through the mediator pipeline.
/// </summary>
/// <remarks>
/// Pipeline execution order: <see cref="UnhandledExceptionBehavior"/> →
/// <see cref="LoggingBehavior"/> → <see cref="ValidationBehavior"/> → Handler.
/// Streaming queries bypass all behaviors.
/// </remarks>
public interface IMediator
{
    /// <summary>Sends a command that returns no value.</summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <returns><see cref="Result.Success"/> or a typed error.</returns>
    Task<Result> Send(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>Sends a command that returns a value.</summary>
    /// <typeparam name="TResponse">The value type returned on success.</typeparam>
    Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
}
```

## README Embedding in Packages

NuGet.org renders the `PackageReadmeFile` on the package listing page. Use package-specific READMEs
rather than the root README when each package has distinct setup steps.

```xml
<!-- Reference a package-level README -->
<PropertyGroup>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>

<ItemGroup>
  <!-- Path relative to the .csproj file -->
  <None Include="README.md" Pack="true" PackagePath="/" />
</ItemGroup>
```

## SourceLink / Symbol Packages

SourceLink ties the NuGet package to exact GitHub commits, enabling debugger step-into. Add to
`Directory.Build.props`:

```xml
<PropertyGroup>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="All" />
</ItemGroup>
```

---

## WARNING: Generic Descriptions

**The Problem:**

```xml
<!-- BAD — could describe any library -->
<Description>A library for .NET applications.</Description>
```

**Why This Breaks:**
1. NuGet.org search ranks packages by keyword match in Description — generic text loses to specific
2. Developers scanning search results can't differentiate from 50 other libraries
3. No keywords means no organic discovery

**The Fix:**

```xml
<!-- GOOD — specific, keyword-rich, differentiating -->
<Description>Custom CQRS mediator for .NET 10 modular monoliths. Pipeline behaviors (validation, logging, metrics, unhandled exceptions), Result pattern with implicit conversions, FluentValidation integration. Zero MediatR dependency.</Description>
```

## WARNING: Missing GenerateDocumentationFile

Without `GenerateDocumentationFile`, consumers see no IntelliSense tooltips. This is a trust signal —
undocumented libraries feel unfinished and unsafe to adopt.

```xml
<!-- In Directory.Build.props — applies to all projects -->
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

Suppress specific warnings on generated code to avoid noise:

```csharp
// On source-generated files (Modulus.Generators output)
#pragma warning disable CS1591 // Missing XML comment for publicly visible type
[GeneratedCode("Modulus.Generators", "1.0.0")]
public static class ModulusHandlerRegistrations { ... }
#pragma warning restore CS1591
```
