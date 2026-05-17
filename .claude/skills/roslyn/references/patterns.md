# Roslyn Patterns Reference

## Contents
- Incremental Generator Pipeline
- Handler Discovery Pattern
- Equatable Structs for Caching
- Analyzer Registration Patterns
- Code Fix Providers
- Strongly-Typed ID Pattern
- Anti-Patterns

---

## Incremental Generator Pipeline

Use `IIncrementalGenerator`, never `ISourceGenerator`. The old API rebuilds everything on every keystroke; incremental pipelines cache intermediate results.

```csharp
[Generator]
public sealed class HandlerRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Collect candidates via syntax predicate (fast — no semantic model)
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax cls &&
                    cls.BaseList is { Types.Count: > 0 } &&
                    !cls.Modifiers.Any(SyntaxKind.AbstractKeyword),
                transform: static (ctx, ct) => GetHandlerInfo(ctx, ct))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        // 2. Combine with build properties (assembly name for namespace)
        var combined = candidates
            .Collect()
            .Combine(context.AnalyzerConfigOptionsProvider);

        // 3. Emit source
        context.RegisterSourceOutput(combined, static (spc, data) =>
            Execute(spc, data.Left, data.Right));
    }
}
```

**Key rule:** The `predicate` lambda must be `static` and must NOT allocate. It runs on every keystroke. The `transform` gets a semantic model — do expensive work here, but keep results equatable.

---

## Handler Discovery Pattern

The `HandlerRegistrationGenerator` detects 6 interface families by walking the `INamedTypeSymbol.Interfaces` collection:

```csharp
private static bool ImplementsHandlerInterface(
    INamedTypeSymbol type,
    INamedTypeSymbol[] handlerInterfaces)
{
    // Check direct interfaces and walk base types
    foreach (var iface in type.AllInterfaces)
    {
        var constructed = iface.IsGenericType
            ? iface.OriginalDefinition
            : iface;

        if (handlerInterfaces.Contains(constructed, SymbolEqualityComparer.Default))
            return true;
    }
    return false;
}
```

For `AbstractValidator<T>`, the generator walks the **base type chain** (not interfaces) because FluentValidation uses inheritance:

```csharp
private static bool IsAbstractValidator(INamedTypeSymbol type)
{
    var baseType = type.BaseType;
    while (baseType is not null)
    {
        if (baseType.Name == "AbstractValidator" &&
            baseType.ContainingNamespace.ToDisplayString() == "FluentValidation")
            return true;
        baseType = baseType.BaseType;
    }
    return false;
}
```

**Open generics are skipped** — the generator emits `MODGEN003` (Info) and moves on. You cannot register `MyHandler<T>` as a scoped service without knowing `T` at compile time.

---

## Equatable Structs for Caching

Incremental generators require pipeline values to be equatable. Non-equatable results break caching and defeat incrementalism. Use immutable equatable structs:

```csharp
// GOOD — equatable, immutable, cache-friendly
internal readonly struct HandlerInfo : IEquatable<HandlerInfo>
{
    public string FullTypeName { get; }
    public string InterfaceTypeName { get; }
    public string Category { get; }

    public HandlerInfo(string fullTypeName, string interfaceTypeName, string category)
    {
        FullTypeName = fullTypeName;
        InterfaceTypeName = interfaceTypeName;
        Category = category;
    }

    public bool Equals(HandlerInfo other) =>
        FullTypeName == other.FullTypeName &&
        InterfaceTypeName == other.InterfaceTypeName;

    public override bool Equals(object? obj) => obj is HandlerInfo h && Equals(h);
    public override int GetHashCode() => HashCode.Combine(FullTypeName, InterfaceTypeName);
}
```

### WARNING: Returning INamedTypeSymbol from Transform

**The Problem:**
```csharp
// BAD — INamedTypeSymbol is not equatable by value
transform: static (ctx, ct) => ctx.SemanticModel.GetDeclaredSymbol(ctx.Node)
```

**Why This Breaks:**
1. Every call returns a new symbol instance — structural equality isn't implemented
2. The cache always invalidates, defeating incrementalism
3. Every edit triggers a full regeneration pass

**The Fix:**
```csharp
// GOOD — extract strings/primitives, return equatable struct
transform: static (ctx, ct) =>
{
    var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) as INamedTypeSymbol;
    if (symbol is null) return null;
    return new HandlerInfo(
        symbol.ToDisplayString(),
        GetInterfaceName(symbol));
}
```

---

## Analyzer Registration Patterns

### CompilationStartAction for Scoped Analyzers

MOD004 only applies to `.Domain` assemblies. Use `RegisterCompilationStartAction` to gate analysis:

```csharp
public override void Initialize(AnalysisContext context)
{
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();

    context.RegisterCompilationStartAction(compilationCtx =>
    {
        // Guard: only run in Domain assemblies
        if (!compilationCtx.Compilation.AssemblyName?.EndsWith(".Domain") == true)
            return;

        compilationCtx.RegisterSyntaxNodeAction(
            AnalyzeAttribute,
            SyntaxKind.Attribute);
    });
}
```

**Always call** `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` — analyzers should not flag generated code (including their own output).

### SyntaxNodeAction vs SymbolAction

Use `RegisterSyntaxNodeAction` when you need syntax context (throw statements, attributes). Use `RegisterSymbolAction` when you need semantic info across the type (handler return type):

```csharp
// MOD003: throw statement — syntax-level
context.RegisterSyntaxNodeAction(AnalyzeThrow, SyntaxKind.ThrowStatement);

// MOD002: return type validation — symbol-level
context.RegisterSymbolAction(AnalyzeHandler, SymbolKind.NamedType);
```

---

## Code Fix Providers

All code fixes follow the same structure: find the node, compute new syntax, replace via `DocumentEditor`.

```csharp
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PublicSetterCodeFixProvider))]
[Shared]
public sealed class PublicSetterCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticDescriptors.MOD005.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics[0];
        var node = root?.FindNode(diagnostic.Location.SourceSpan);

        if (node is not AccessorDeclarationSyntax setter) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add 'private' modifier",
                createChangedDocument: ct => AddPrivateModifier(context.Document, setter, ct),
                equivalenceKey: "AddPrivateModifier"),
            diagnostic);
    }

    private static async Task<Document> AddPrivateModifier(
        Document document,
        AccessorDeclarationSyntax setter,
        CancellationToken ct)
    {
        var editor = await DocumentEditor.CreateAsync(document, ct);
        var privateToken = SyntaxFactory.Token(SyntaxKind.PrivateKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        editor.ReplaceNode(setter,
            setter.AddModifiers(privateToken));

        return editor.GetChangedDocument();
    }
}
```

**Always implement `GetFixAllProvider()`** — IDEs use this to apply the fix across the entire solution at once.

---

## Strongly-Typed ID Pattern

`[StronglyTypedId]` requires `partial record struct`. The generator enforces this with two diagnostics before emitting code:

```csharp
// MODGEN001: must be partial
// MODGEN002: must be record struct (not class, not plain struct)

// CORRECT
[StronglyTypedId]
public partial record struct ProductId;

// MODGEN001: missing partial
[StronglyTypedId]
public record struct ProductId;  // ❌

// MODGEN002: wrong type kind
[StronglyTypedId]
public partial class ProductId { }  // ❌
```

Generated members (Guid-backed):
```csharp
// Auto-generated inside ProductId partial:
public static ProductId New() => new(Guid.NewGuid());
public static ProductId Empty => new(Guid.Empty);

public sealed class ValueConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<ProductId, Guid> { ... }
public sealed class JsonConverter : System.Text.Json.Serialization.JsonConverter<ProductId> { ... }
public sealed class TypeConverter : System.ComponentModel.TypeConverter { ... }
```

---

## Anti-Patterns

### WARNING: Using RegisterCompilationAction in a Generator

**The Problem:**
```csharp
// BAD — full rebuild on every change
public void Execute(GeneratorExecutionContext context) { ... }
```

**Why This Breaks:** `ISourceGenerator` (old API) runs everything on every compilation. In a solution with 50 handlers, every keystroke triggers full re-scan. `IIncrementalGenerator` caches by pipeline node.

**The Fix:** Always implement `IIncrementalGenerator`.

### WARNING: Skipping EnableConcurrentExecution

**The Problem:**
```csharp
// BAD — analyzers run single-threaded
public override void Initialize(AnalysisContext context)
{
    context.RegisterSyntaxNodeAction(...);
}
```

**Why This Breaks:** Roslyn parallelizes compilation. Without `EnableConcurrentExecution()`, your analyzer becomes a bottleneck. All analyzer actions must be thread-safe (no shared mutable state).

**The Fix:**
```csharp
context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
context.EnableConcurrentExecution();
```
