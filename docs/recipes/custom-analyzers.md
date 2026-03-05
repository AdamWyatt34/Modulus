# Custom Roslyn Analyzers

## Problem

Your team has coding conventions that go beyond what the built-in Modulus analyzers (MOD001--MOD005) enforce. For example, you want to ensure all command names end with "Command", all queries are sealed, or that certain namespaces are never used in specific layers.

## Solution

Create a custom Roslyn analyzer following the same patterns used by the Modulus analyzers. The analyzer runs in the IDE and during builds, providing instant feedback when conventions are violated.

### Step 1: Create the Analyzer Project

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

### Step 2: Define the Diagnostic Descriptor

```csharp
public static class TeamDiagnostics
{
    public static readonly DiagnosticDescriptor CommandNaming = new(
        id: "TEAM001",
        title: "Command type name should end with 'Command'",
        messageFormat: "Type '{0}' implements ICommand but does not end with 'Command'",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
```

### Step 3: Implement the Analyzer

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CommandNamingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [TeamDiagnostics.CommandNaming];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.IsAbstract)
            return;

        var implementsCommand = type.AllInterfaces.Any(i =>
            i.Name is "ICommand" &&
            i.ContainingNamespace.ToDisplayString() == "Modulus.Mediator.Abstractions.Messaging");

        if (implementsCommand && !type.Name.EndsWith("Command"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                TeamDiagnostics.CommandNaming,
                type.Locations[0],
                type.Name));
        }
    }
}
```

### Step 4: Add a Code Fix (Optional)

```csharp
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Rename;

[ExportCodeFixProvider(LanguageNames.CSharp)]
[Shared]
public class CommandNamingCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ["TEAM001"];

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var node = root?.FindNode(diagnostic.Location.SourceSpan);

        if (node is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
        var symbol = semanticModel?.GetDeclaredSymbol(node, context.CancellationToken);

        if (symbol is null) return;

        var newName = symbol.Name + "Command";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Rename to '{newName}'",
                createChangedSolution: ct =>
                    Renamer.RenameSymbolAsync(
                        context.Document.Project.Solution,
                        symbol,
                        new SymbolRenameOptions(),
                        newName,
                        ct),
                equivalenceKey: "TEAM001_Fix"),
            diagnostic);
    }
}
```

### Step 5: Reference the Analyzer

```xml
<ProjectReference Include="..\YourAnalyzers\YourAnalyzers.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### Step 6: Test the Analyzer

```csharp
[Fact]
public async Task CommandWithoutSuffix_ReportsDiagnostic()
{
    var source = """
        using Modulus.Mediator.Abstractions.Messaging;
        public record PlaceOrder : ICommand;
        """;

    var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
        new CommandNamingAnalyzer(), source);

    diagnostics.Length.ShouldBe(1);
    diagnostics[0].Id.ShouldBe("TEAM001");
}
```

## Tips

- **Start with `RegisterSymbolAction`** for type-level checks (naming, interfaces, inheritance)
- **Use `RegisterSyntaxNodeAction`** for statement-level checks (throw statements, method calls)
- **Reference the Modulus analyzers** as examples: `ModuleBoundaryAnalyzer`, `HandlerReturnTypeAnalyzer`, etc.
- **Always call `EnableConcurrentExecution()`** for performance
- **Test with `AnalyzerTestHelper`** -- see `tests/Modulus.Analyzers.Tests/` for a working test harness

## See Also

- [Analyzers Overview](/analyzers/) -- Built-in Modulus analyzer rules
- [Analyzer Configuration](/analyzers/configuration) -- How users configure analyzer severities
- [Microsoft: Roslyn Analyzers](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix) -- Official tutorial
