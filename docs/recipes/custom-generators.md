# Custom Source Generators

## Problem

Your team has domain-specific boilerplate patterns that repeat across modules. For example, every aggregate root needs a factory method, every repository interface follows the same shape, or every integration event needs a corresponding handler stub. You want to automate these patterns the same way Modulus automates handler registration and strongly typed IDs.

## Solution

Create a custom Roslyn incremental source generator that follows the same patterns used by the Modulus generators. The generator runs at compile time, discovers annotated types, and produces the boilerplate code automatically.

### Step 1: Create the Generator Project

Create a new class library targeting `netstandard2.0`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

::: warning Target framework
Source generators **must** target `netstandard2.0`. This is a Roslyn requirement -- generators run inside the compiler process, which loads `netstandard2.0` assemblies.
:::

### Step 2: Define a Marker Attribute

Create an attribute in your abstractions project that users will apply to trigger generation:

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GenerateRepositoryAttribute : Attribute { }
```

### Step 3: Implement the Generator

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Generator]
public class RepositoryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider.ForAttributeWithMetadataName(
            "YourNamespace.GenerateRepositoryAttribute",
            predicate: (node, _) => node is ClassDeclarationSyntax,
            transform: (ctx, _) => GetModel(ctx));

        context.RegisterSourceOutput(provider, GenerateSource);
    }

    private static EntityModel GetModel(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        return new EntityModel(
            symbol.Name,
            symbol.ContainingNamespace.ToDisplayString());
    }

    private static void GenerateSource(
        SourceProductionContext context, EntityModel model)
    {
        var source = $$"""
            namespace {{model.Namespace}};

            public interface I{{model.Name}}Repository
            {
                Task<{{model.Name}}?> GetByIdAsync(
                    {{model.Name}}Id id, CancellationToken ct = default);
                Task AddAsync(
                    {{model.Name}} entity, CancellationToken ct = default);
            }
            """;

        context.AddSource($"I{model.Name}Repository.g.cs", source);
    }

    private record EntityModel(string Name, string Namespace);
}
```

### Step 4: Reference the Generator

In consuming projects, reference your generator as an analyzer:

```xml
<ProjectReference Include="..\YourGenerators\YourGenerators.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### Step 5: Use It

```csharp
[GenerateRepository]
public class Product : AggregateRoot<ProductId>
{
    // ...
}

// Generated: IProductRepository with GetByIdAsync and AddAsync
```

## Tips

- **Use `ForAttributeWithMetadataName`** for attribute-triggered generators -- it is the most efficient filtering API
- **Keep generator logic pure** -- extract metadata into an `Equatable` model, then generate from the model. This enables the incremental cache to skip re-generation when inputs haven't changed
- **Test with `CSharpGeneratorDriver`** -- see `tests/Modulus.Generators.Tests/GeneratorTestHelper.cs` for a working test harness
- **Reference the Modulus generators** as examples: `StronglyTypedIdGenerator`, `HandlerRegistrationGenerator`, and `ModuleRegistrationGenerator`

## See Also

- [Source Generators Overview](/generators/) -- How Modulus generators work
- [Microsoft: Incremental Generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md) -- Official Roslyn documentation
