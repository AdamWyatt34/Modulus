using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Modulus.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerReturnTypeAnalyzer : DiagnosticAnalyzer
{
    private const string MediatorNamespace = "Modulus.Mediator.Abstractions";

    // Metadata names: ICommandHandler`1, ICommandHandler`2, IQueryHandler`2
    private static readonly ImmutableHashSet<string> HandlerMetadataNames = ImmutableHashSet.Create(
        "ICommandHandler`1",
        "ICommandHandler`2",
        "IQueryHandler`2");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.HandlerReturnTypeViolation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;

        // Skip abstract and open generic types
        if (typeSymbol.IsAbstract || typeSymbol.IsGenericType)
            return;

        foreach (var iface in typeSymbol.AllInterfaces)
        {
            var originalDef = iface.OriginalDefinition;
            var ns = originalDef.ContainingNamespace?.ToDisplayString();
            var metadataName = originalDef.MetadataName;

            if (ns != MediatorNamespace || !HandlerMetadataNames.Contains(metadataName))
                continue;

            // Found a handler interface — check the Handle method
            var handleMethod = typeSymbol.GetMembers("Handle")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => !m.IsStatic);

            if (handleMethod is null)
                continue;

            if (!IsValidHandlerReturnType(handleMethod.ReturnType))
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.HandlerReturnTypeViolation,
                    handleMethod.Locations.FirstOrDefault() ?? typeSymbol.Locations.FirstOrDefault(),
                    typeSymbol.Name);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsValidHandlerReturnType(ITypeSymbol returnType)
    {
        // Must be Task<T>
        if (returnType is not INamedTypeSymbol { Name: "Task", Arity: 1 } taskType)
            return false;

        var typeArg = taskType.TypeArguments[0];

        // Type argument must be Result or Result<T> from Modulus.Mediator.Abstractions
        var ns = typeArg.ContainingNamespace?.ToDisplayString();
        if (ns is null || !(ns == MediatorNamespace || ns.StartsWith(MediatorNamespace + ".")))
            return false;

        return typeArg.Name == "Result";
    }
}