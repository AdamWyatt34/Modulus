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
            .Where(static r => r.Registrations.Length > 0)
            .SelectMany(static (r, _) => r.Registrations);

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

        var namespaceInfo = rootNamespace.Combine(assemblyName);
        var combined = collected.Combine(namespaceInfo);

        context.RegisterSourceOutput(combined, static (spc, data) =>
            Execute(spc, data.Left, data.Right.Left, data.Right.Right));

        // Diagnostic pipeline — extract open generic diagnostics from the same scan
        var openGenericDiagnostics = candidateProvider
            .Where(static r => r.OpenGenericDiagnostic is not null)
            .Select(static (r, _) => r.OpenGenericDiagnostic!);

        context.RegisterSourceOutput(openGenericDiagnostics, static (spc, diag) =>
            spc.ReportDiagnostic(diag));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDecl)
            return false;

        foreach (var modifier in classDecl.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.AbstractKeyword) ||
                modifier.IsKind(SyntaxKind.StaticKeyword))
                return false;
        }

        return classDecl.BaseList is not null && classDecl.BaseList.Types.Count > 0;
    }

    private static CandidateResult AnalyzeCandidate(
        GeneratorSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);

        if (symbol is null || symbol.IsAbstract || symbol.IsStatic)
            return default;

        // Open generic types get a diagnostic instead of registrations
        if (symbol.IsGenericType)
        {
            var diag = GetOpenGenericDiagnostic(classDecl, symbol);
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

    private static EquatableArray<HandlerRegistration> FindHandlersInReferencedAssemblies(
        Compilation compilation, CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<HandlerRegistration>();

        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ct.ThrowIfCancellationRequested();
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

    private static Diagnostic? GetOpenGenericDiagnostic(
        ClassDeclarationSyntax classDecl,
        INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (TryGetHandlerCategory(iface, out _))
            {
                return Diagnostic.Create(
                    DiagnosticDescriptors.OpenGenericHandlerSkipped,
                    classDecl.Identifier.GetLocation(),
                    symbol.Name);
            }
        }

        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            if (IsAbstractValidator(baseType))
            {
                return Diagnostic.Create(
                    DiagnosticDescriptors.OpenGenericHandlerSkipped,
                    classDecl.Identifier.GetLocation(),
                    symbol.Name);
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
        sb.AppendLine("public static class ModulusHandlerRegistrations");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddModulusHandlers(this IServiceCollection services)");
        sb.AppendLine("    {");

        if (registrations.Length > 0)
        {
            var grouped = registrations.Array
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

internal readonly struct CandidateResult
{
    public ImmutableArray<HandlerRegistration> Registrations { get; }
    public Diagnostic? OpenGenericDiagnostic { get; }

    public CandidateResult(ImmutableArray<HandlerRegistration> registrations, Diagnostic? openGenericDiagnostic)
    {
        Registrations = registrations.IsDefault ? ImmutableArray<HandlerRegistration>.Empty : registrations;
        OpenGenericDiagnostic = openGenericDiagnostic;
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