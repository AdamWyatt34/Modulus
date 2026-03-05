using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Modulus.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublicSetterOnEntityAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.PublicSetterOnEntity);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;

        // Find the set accessor (not init)
        var setAccessor = property.AccessorList?.Accessors
            .FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));

        if (setAccessor is null)
            return;

        // Skip if the set accessor has an explicit access modifier (private, protected, internal)
        if (setAccessor.Modifiers.Count > 0)
            return;

        // Check if the property itself is public
        if (!property.Modifiers.Any(SyntaxKind.PublicKeyword))
            return;

        // Check if the containing type inherits from Entity or AggregateRoot
        var containingType = context.SemanticModel.GetDeclaredSymbol(property)?.ContainingType;
        if (containingType is null || !InheritsFromEntityOrAggregateRoot(containingType))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.PublicSetterOnEntity,
            setAccessor.Keyword.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private static bool InheritsFromEntityOrAggregateRoot(INamedTypeSymbol type)
    {
        var baseType = type.BaseType;
        while (baseType is not null)
        {
            if (baseType.Name == "Entity" || baseType.Name == "AggregateRoot")
                return true;

            baseType = baseType.BaseType;
        }

        return false;
    }
}