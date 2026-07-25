using System.Collections.Generic;
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

        // A type can implement more than one handler interface (e.g. a command handler and a
        // query handler side by side), each with its own "Handle" overload. Resolving via
        // `GetMembers("Handle").First()` picked an arbitrary overload regardless of which
        // interface was being checked, so it could check the wrong method — or the same method —
        // for every interface. Track methods already checked so an offending method is only
        // reported once even if it satisfies multiple interfaces.
        HashSet<IMethodSymbol>? checkedMethods = null;

        foreach (var iface in typeSymbol.AllInterfaces)
        {
            var originalDef = iface.OriginalDefinition;
            var ns = originalDef.ContainingNamespace?.ToDisplayString();
            var metadataName = originalDef.MetadataName;

            if (ns != MediatorNamespace || !HandlerMetadataNames.Contains(metadataName))
                continue;

            var interfaceHandleMethod = iface.GetMembers("Handle").OfType<IMethodSymbol>().FirstOrDefault();
            if (interfaceHandleMethod is null)
                continue;

            var handleMethod = ResolveHandleMethod(typeSymbol, interfaceHandleMethod);
            if (handleMethod is null)
                continue;

            checkedMethods ??= new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            if (!checkedMethods.Add(handleMethod))
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

    private static IMethodSymbol? ResolveHandleMethod(INamedTypeSymbol typeSymbol, IMethodSymbol interfaceHandleMethod)
    {
        // The common case: a correctly implemented (implicit or explicit) interface member. This
        // correctly disambiguates a type implementing more than one handler interface, where a
        // naive `GetMembers("Handle").First()` could pick an arbitrary — possibly unrelated —
        // overload regardless of which interface was being checked.
        if (typeSymbol.FindImplementationForInterfaceMember(interfaceHandleMethod) is IMethodSymbol implementation)
            return implementation;

        // No valid implementation exists — most commonly because the return type doesn't match
        // `Task<Result>`/`Task<Result<T>>` at all, which is the exact violation this analyzer
        // exists to catch (and is itself a compile error, since the interface then goes
        // unimplemented). Fall back to matching by parameter shape only (ignoring return type) so
        // that method is still found and reported instead of silently skipped.
        foreach (var candidate in typeSymbol.GetMembers("Handle").OfType<IMethodSymbol>())
        {
            if (!candidate.IsStatic && ParametersMatch(candidate.Parameters, interfaceHandleMethod.Parameters))
                return candidate;
        }

        return null;
    }

    private static bool ParametersMatch(
        ImmutableArray<IParameterSymbol> candidateParameters,
        ImmutableArray<IParameterSymbol> interfaceParameters)
    {
        if (candidateParameters.Length != interfaceParameters.Length)
            return false;

        for (var i = 0; i < candidateParameters.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidateParameters[i].Type, interfaceParameters[i].Type))
                return false;
        }

        return true;
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
