using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Modulus.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ModuleBoundaryAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> KnownLayers = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Domain",
        "Application",
        "Infrastructure",
        "Integration",
        "Api");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ModuleBoundaryViolation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var moduleInfo = ParseModuleInfo(compilationContext.Compilation.AssemblyName);
            if (moduleInfo is null)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeUsingDirective(ctx, moduleInfo),
                SyntaxKind.UsingDirective);
        });
    }

    private static void AnalyzeUsingDirective(
        SyntaxNodeAnalysisContext context,
        ModuleInfo moduleInfo)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;
        var usingName = usingDirective.Name?.ToString();

        if (usingName is null)
            return;

        if (IsModuleBoundaryViolation(usingName, moduleInfo))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.ModuleBoundaryViolation,
                usingDirective.GetLocation(),
                moduleInfo.ModuleName,
                usingName);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsModuleBoundaryViolation(string namespaceName, ModuleInfo moduleInfo)
    {
        // Must start with the same prefix
        if (!namespaceName.StartsWith(moduleInfo.Prefix + ".", StringComparison.Ordinal))
            return false;

        var afterPrefix = namespaceName.Substring(moduleInfo.Prefix.Length + 1);

        // Allow BuildingBlocks
        if (afterPrefix.StartsWith("BuildingBlocks", StringComparison.Ordinal))
            return false;

        // Allow our own module
        if (afterPrefix.StartsWith(moduleInfo.ModuleName + ".", StringComparison.Ordinal)
            || afterPrefix == moduleInfo.ModuleName)
            return false;

        // Parse the other module name from the remaining namespace
        var dotIndex = afterPrefix.IndexOf('.');
        if (dotIndex < 0)
            return false; // Just a module name without a layer — not a violation

        var otherModule = afterPrefix.Substring(0, dotIndex);
        var afterModule = afterPrefix.Substring(dotIndex + 1);

        // Allow Integration references
        if (afterModule == "Integration" || afterModule.StartsWith("Integration.", StringComparison.Ordinal))
            return false;

        // Check if the layer after the other module name is a known layer
        var layerDot = afterModule.IndexOf('.');
        var layer = layerDot >= 0 ? afterModule.Substring(0, layerDot) : afterModule;

        if (KnownLayers.Contains(layer) && layer != "Integration")
            return true;

        return false;
    }

    internal static ModuleInfo? ParseModuleInfo(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        var parts = assemblyName!.Split('.');
        if (parts.Length < 2)
            return null;

        var lastPart = parts[parts.Length - 1];
        if (!KnownLayers.Contains(lastPart))
            return null;

        var moduleName = parts[parts.Length - 2];
        var prefix = string.Join(".", parts.Take(parts.Length - 2));

        if (string.IsNullOrEmpty(prefix))
            return null;

        return new ModuleInfo(prefix, moduleName, lastPart);
    }

    internal sealed class ModuleInfo(string prefix, string moduleName, string layer)
    {
        public string Prefix { get; } = prefix;
        public string ModuleName { get; } = moduleName;
        public string Layer { get; } = layer;
    }
}