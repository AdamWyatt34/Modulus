using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Modulus.Generators;

[Generator]
public sealed class HandlerRegistrationGenerator : IIncrementalGenerator
{
    private static readonly Dictionary<(string Namespace, string MetadataName), HandlerCategory> KnownInterfaces =
        new Dictionary<(string, string), HandlerCategory>
        {
            { ("Modulus.Mediator.Abstractions", "ICommandHandler`1"), HandlerCategory.Command },
            { ("Modulus.Mediator.Abstractions", "ICommandHandler`2"), HandlerCategory.Command },
            { ("Modulus.Mediator.Abstractions", "IQueryHandler`2"), HandlerCategory.Query },
            { ("Modulus.Mediator.Abstractions", "IStreamQueryHandler`2"), HandlerCategory.StreamQuery },
            { ("Modulus.Mediator.Abstractions", "IDomainEventHandler`1"), HandlerCategory.DomainEvent },
            { ("Modulus.Messaging.Abstractions", "IIntegrationEventHandler`1"), HandlerCategory.IntegrationEvent },
        };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Pipeline 1: Scan syntax trees in the current compilation (existing behavior)
        var candidateProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidate(node),
                transform: static (ctx, ct) => AnalyzeCandidate(ctx, ct));

        var handlerProvider = candidateProvider
            .Where(static r => !r.Registrations.IsEmpty)
            .SelectMany(static (r, _) => r.Registrations.Array);

        var localHandlers = handlerProvider.Collect()
            .Select(static (arr, _) => new EquatableArray<HandlerRegistration>(arr));

        // Pipeline 2: Scan referenced assemblies for handler types
        var referencedHandlers = context.CompilationProvider
            .Select(static (compilation, ct) => FindHandlersInReferencedAssemblies(compilation, ct));

        // Merge both pipelines
        var collected = localHandlers.Combine(referencedHandlers)
            .Select(static (pair, _) =>
            {
                var merged = pair.Left.Array.AddRange(pair.Right.Array);
                return new EquatableArray<HandlerRegistration>(merged);
            });

        var rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns);
                return ns;
            });

        var assemblyName = context.CompilationProvider
            .Select(static (c, _) => c.AssemblyName);

        // The generated file references IServiceCollection, so emit nothing in compilations
        // without a DI reference — e.g. Domain projects that carry this generator solely for
        // [StronglyTypedId]. Same gating pattern as the EF check in StronglyTypedIdGenerator.
        var hasDependencyInjection = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection") is not null);

        var namespaceInfo = rootNamespace.Combine(assemblyName);
        var combined = collected.Combine(namespaceInfo).Combine(hasDependencyInjection);

        context.RegisterSourceOutput(combined, static (spc, data) =>
        {
            if (!data.Right)
                return;

            Execute(spc, data.Left.Left, data.Left.Right.Left, data.Left.Right.Right);
        });

        // Diagnostic pipeline — extract open generic diagnostics from the same scan
        var openGenericDiagnostics = candidateProvider
            .Where(static r => r.OpenGenericDiagnostic is not null)
            .Select(static (r, _) => r.OpenGenericDiagnostic!.Value);

        context.RegisterSourceOutput(openGenericDiagnostics, static (spc, diagInfo) =>
            spc.ReportDiagnostic(diagInfo.ToDiagnostic()));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        // Accept both `class` and `record class` declarations. Record structs are excluded —
        // handlers must be reference types so the DI container can resolve them as scoped services.
        if (node is not TypeDeclarationSyntax typeDecl)
            return false;

        switch (typeDecl)
        {
            case ClassDeclarationSyntax:
                break;
            case RecordDeclarationSyntax record when !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword):
                break;
            default:
                return false;
        }

        foreach (var modifier in typeDecl.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.AbstractKeyword) ||
                modifier.IsKind(SyntaxKind.StaticKeyword))
                return false;
        }

        return typeDecl.BaseList is not null && typeDecl.BaseList.Types.Count > 0;
    }

    private static CandidateResult AnalyzeCandidate(
        GeneratorSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var typeDecl = (TypeDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;

        if (symbol is null || symbol.IsAbstract || symbol.IsStatic)
            return CandidateResult.Empty;

        // A handler nested as private/protected (or declared `file`-local) cannot be named from
        // the generated static class — registering it would produce CS0122/CS0246.
        if (!IsAccessibleFromGeneratedCode(symbol))
            return CandidateResult.Empty;

        // Open generic types get a diagnostic instead of registrations
        if (symbol.IsGenericType)
        {
            var diag = GetOpenGenericDiagnostic(typeDecl, symbol);
            return new CandidateResult(ImmutableArray<HandlerRegistration>.Empty, diag);
        }

        var builder = ImmutableArray.CreateBuilder<HandlerRegistration>();
        var handlerFqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        foreach (var iface in symbol.AllInterfaces)
        {
            ct.ThrowIfCancellationRequested();
            if (TryGetHandlerCategory(iface, out var category))
            {
                var ifaceFqn = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.Add(new HandlerRegistration(handlerFqn, ifaceFqn, category));
            }
        }

        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (IsAbstractValidator(baseType) && baseType.TypeArguments.Length == 1)
            {
                var validatedType = baseType.TypeArguments[0];
                var validatedFqn = validatedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var iValidatorFqn = $"global::FluentValidation.IValidator<{validatedFqn}>";
                builder.Add(new HandlerRegistration(handlerFqn, iValidatorFqn, HandlerCategory.Validator));
                break;
            }
            baseType = baseType.BaseType;
        }

        return new CandidateResult(
            builder.Count > 0 ? builder.ToImmutable() : ImmutableArray<HandlerRegistration>.Empty,
            null);
    }

    /// <summary>
    /// A type is only nameable from the generated top-level static class when every containing
    /// type (and the type itself) is public or internal. <c>private</c>/<c>protected</c>/
    /// <c>private protected</c> nesting and C# 11 <c>file</c>-local types are all invisible from
    /// unrelated generated code, even though they satisfy <see cref="IsCandidate"/> at the syntax
    /// level.
    /// </summary>
    private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol symbol)
    {
        if (symbol.IsFileLocal)
            return false;

        for (var type = symbol; type is not null; type = type.ContainingType)
        {
            switch (type.DeclaredAccessibility)
            {
                case Accessibility.Private:
                case Accessibility.Protected:
                case Accessibility.ProtectedAndInternal:
                    return false;
            }
        }

        return true;
    }

    private static EquatableArray<HandlerRegistration> FindHandlersInReferencedAssemblies(
        Compilation compilation, CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<HandlerRegistration>();

        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ct.ThrowIfCancellationRequested();

            // Skip the BCL/framework closure and any assembly that couldn't possibly define a
            // Modulus handler — the dominant cost of this walk otherwise (H-GEN5).
            if (!ReferencedAssemblyFilter.ShouldWalk(assemblySymbol))
                continue;

            CollectHandlersFromNamespace(assemblySymbol.GlobalNamespace, builder, ct);
        }

        return new EquatableArray<HandlerRegistration>(builder.ToImmutable());
    }

    private static void CollectHandlersFromNamespace(
        INamespaceSymbol ns,
        ImmutableArray<HandlerRegistration>.Builder builder,
        CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (type.IsAbstract || type.IsStatic || type.IsGenericType)
                continue;

            // A handler in another assembly must be public (an internal type is inaccessible to
            // the host assembly generating the registration, absent InternalsVisibleTo — CS0122)
            // and must be a reference type (DI's `AddScoped<TService, TImplementation>` requires
            // `TImplementation : class`, so a record struct would be CS0452).
            if (type.DeclaredAccessibility != Accessibility.Public || type.TypeKind == TypeKind.Struct)
                continue;

            var handlerFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            foreach (var iface in type.AllInterfaces)
            {
                if (TryGetHandlerCategory(iface, out var category))
                {
                    var ifaceFqn = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    builder.Add(new HandlerRegistration(handlerFqn, ifaceFqn, category));
                }
            }

            var baseType = type.BaseType;
            while (baseType is not null)
            {
                if (IsAbstractValidator(baseType) && baseType.TypeArguments.Length == 1)
                {
                    var validatedType = baseType.TypeArguments[0];
                    var validatedFqn = validatedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var iValidatorFqn = $"global::FluentValidation.IValidator<{validatedFqn}>";
                    builder.Add(new HandlerRegistration(handlerFqn, iValidatorFqn, HandlerCategory.Validator));
                    break;
                }
                baseType = baseType.BaseType;
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            CollectHandlersFromNamespace(childNs, builder, ct);
        }
    }

    private static bool TryGetHandlerCategory(INamedTypeSymbol iface, out HandlerCategory category)
    {
        var originalDef = iface.OriginalDefinition;
        var metadataName = originalDef.MetadataName;
        var ns = originalDef.ContainingNamespace?.ToDisplayString();

        if (ns is not null && KnownInterfaces.TryGetValue((ns, metadataName), out category))
            return true;

        category = default;
        return false;
    }

    private static bool IsAbstractValidator(INamedTypeSymbol type)
    {
        var originalDef = type.OriginalDefinition;
        return originalDef.MetadataName == "AbstractValidator`1" &&
               originalDef.ContainingNamespace?.ToDisplayString() == "FluentValidation";
    }

    private static DiagnosticInfo? GetOpenGenericDiagnostic(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (TryGetHandlerCategory(iface, out _))
            {
                return DiagnosticInfo.FromDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.OpenGenericHandlerSkipped,
                    typeDecl.Identifier.GetLocation(),
                    symbol.Name));
            }
        }

        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            if (IsAbstractValidator(baseType))
            {
                return DiagnosticInfo.FromDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.OpenGenericHandlerSkipped,
                    typeDecl.Identifier.GetLocation(),
                    symbol.Name));
            }
            baseType = baseType.BaseType;
        }

        return null;
    }

    private static void Execute(
        SourceProductionContext context,
        EquatableArray<HandlerRegistration> registrations,
        string? rootNamespace,
        string? assemblyName)
    {
        var ns = rootNamespace ?? assemblyName ?? "GeneratedRegistrations";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"[global::System.CodeDom.Compiler.GeneratedCode(\"Modulus.Generators.HandlerRegistrationGenerator\", \"{GeneratorVersion.Value}\")]");
        sb.AppendLine("public static class ModulusHandlerRegistrations");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddModulusHandlers(this IServiceCollection services)");
        sb.AppendLine("    {");

        if (!registrations.IsEmpty)
        {
            // Distinct — a partial-class handler shape can surface the same (handler, interface)
            // pair more than once from the local syntax scan (once per partial declaration that
            // carries the base list). Without this, the same handler is registered twice and a
            // domain-event handler/validator resolved via `GetServices<T>()` runs twice per event.
            var grouped = registrations.Array
                .Distinct()
                .OrderBy(r => r.Category)
                .ThenBy(r => r.HandlerFullyQualifiedName, StringComparer.Ordinal)
                .GroupBy(r => r.Category);

            var first = true;
            foreach (var group in grouped)
            {
                if (!first)
                    sb.AppendLine();
                first = false;

                sb.AppendLine($"        // {GetCategoryComment(group.Key)}");
                foreach (var reg in group)
                {
                    sb.AppendLine($"        services.AddScoped<{reg.InterfaceFullyQualifiedName}, {reg.HandlerFullyQualifiedName}>();");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource(
            "ModulusHandlerRegistrations.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string GetCategoryComment(HandlerCategory category)
    {
        switch (category)
        {
            case HandlerCategory.Command: return "Commands";
            case HandlerCategory.Query: return "Queries";
            case HandlerCategory.StreamQuery: return "Stream Queries";
            case HandlerCategory.DomainEvent: return "Domain Events";
            case HandlerCategory.IntegrationEvent: return "Integration Events";
            case HandlerCategory.Validator: return "Validators";
            default: return "Other";
        }
    }
}

internal enum HandlerCategory
{
    Command,
    Query,
    StreamQuery,
    DomainEvent,
    IntegrationEvent,
    Validator
}

internal readonly struct CandidateResult : IEquatable<CandidateResult>
{
    public static readonly CandidateResult Empty = new(ImmutableArray<HandlerRegistration>.Empty, null);

    public EquatableArray<HandlerRegistration> Registrations { get; }
    public DiagnosticInfo? OpenGenericDiagnostic { get; }

    public CandidateResult(ImmutableArray<HandlerRegistration> registrations, DiagnosticInfo? openGenericDiagnostic)
    {
        Registrations = new EquatableArray<HandlerRegistration>(registrations);
        OpenGenericDiagnostic = openGenericDiagnostic;
    }

    public bool Equals(CandidateResult other) =>
        Registrations.Equals(other.Registrations) &&
        Equals(OpenGenericDiagnostic, other.OpenGenericDiagnostic);

    public override bool Equals(object obj) =>
        obj is CandidateResult other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Registrations.GetHashCode();
            hash = (hash * 397) ^ (OpenGenericDiagnostic?.GetHashCode() ?? 0);
            return hash;
        }
    }
}

internal readonly struct HandlerRegistration : IEquatable<HandlerRegistration>
{
    public string HandlerFullyQualifiedName { get; }
    public string InterfaceFullyQualifiedName { get; }
    public HandlerCategory Category { get; }

    public HandlerRegistration(
        string handlerFullyQualifiedName,
        string interfaceFullyQualifiedName,
        HandlerCategory category)
    {
        HandlerFullyQualifiedName = handlerFullyQualifiedName;
        InterfaceFullyQualifiedName = interfaceFullyQualifiedName;
        Category = category;
    }

    public bool Equals(HandlerRegistration other) =>
        HandlerFullyQualifiedName == other.HandlerFullyQualifiedName &&
        InterfaceFullyQualifiedName == other.InterfaceFullyQualifiedName &&
        Category == other.Category;

    public override bool Equals(object obj) =>
        obj is HandlerRegistration other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = HandlerFullyQualifiedName?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (InterfaceFullyQualifiedName?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (int)Category;
            return hash;
        }
    }
}
