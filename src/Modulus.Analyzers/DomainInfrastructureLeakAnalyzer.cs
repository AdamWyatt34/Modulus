using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Modulus.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DomainInfrastructureLeakAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> ForbiddenAttributeNamespaces = ImmutableHashSet.Create(
        "System.ComponentModel.DataAnnotations",
        "System.ComponentModel.DataAnnotations.Schema",
        "Microsoft.EntityFrameworkCore",
        "System.Text.Json.Serialization",
        "Newtonsoft.Json");

    private static readonly ImmutableHashSet<string> ForbiddenUsingPrefixes = ImmutableHashSet.Create(
        "Microsoft.EntityFrameworkCore",
        "Newtonsoft.Json");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.DomainInfrastructureLeak);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var assemblyName = compilationContext.Compilation.AssemblyName;
            if (assemblyName is null)
                return;

            var parts = assemblyName.Split('.');
            if (parts.Length < 2 || parts[parts.Length - 1] != "Domain")
                return;

            compilationContext.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
            compilationContext.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        });
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;

        var typeInfo = context.SemanticModel.GetTypeInfo(attribute, context.CancellationToken);
        var attributeType = typeInfo.Type;
        if (attributeType is null)
            return;

        var containingNamespace = attributeType.ContainingNamespace?.ToDisplayString();
        if (containingNamespace is null)
            return;

        if (!ForbiddenAttributeNamespaces.Contains(containingNamespace))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.DomainInfrastructureLeak,
            attribute.GetLocation(),
            attributeType.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;

        var usingName = usingDirective.Name?.ToString();
        if (usingName is null)
            return;

        foreach (var prefix in ForbiddenUsingPrefixes)
        {
            if (usingName == prefix || usingName.StartsWith(prefix + "."))
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.DomainInfrastructureLeak,
                    usingDirective.GetLocation(),
                    usingName);

                context.ReportDiagnostic(diagnostic);
                return;
            }
        }
    }
}