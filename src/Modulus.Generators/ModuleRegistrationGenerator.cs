using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Modulus.Generators;

[Generator]
public sealed class ModuleRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modulesProvider = context.CompilationProvider
            .Select(static (compilation, ct) => FindModuleRegistrations(compilation, ct));

        var rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns);
                return ns;
            });

        var assemblyName = context.CompilationProvider
            .Select(static (c, _) => c.AssemblyName);

        var namespaceInfo = rootNamespace.Combine(assemblyName);
        var combined = modulesProvider.Combine(namespaceInfo);

        context.RegisterSourceOutput(combined, static (spc, data) =>
            Execute(spc, data.Left, data.Right.Left, data.Right.Right));

        var diagnosticsProvider = context.CompilationProvider
            .SelectMany(static (compilation, ct) => FindIncompleteModules(compilation, ct));

        context.RegisterSourceOutput(diagnosticsProvider, static (spc, diag) =>
            spc.ReportDiagnostic(diag));
    }

    private static EquatableArray<ModuleRegistrationModel> FindModuleRegistrations(
        Compilation compilation, CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<ModuleRegistrationModel>();

        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ct.ThrowIfCancellationRequested();
            CollectModulesFromNamespace(assemblySymbol.GlobalNamespace, builder, ct);
        }

        var sorted = builder
            .OrderBy(m => m.Order)
            .ThenBy(m => m.FullyQualifiedName, StringComparer.Ordinal)
            .ToImmutableArray();

        return new EquatableArray<ModuleRegistrationModel>(sorted);
    }

    private static void CollectModulesFromNamespace(
        INamespaceSymbol ns,
        ImmutableArray<ModuleRegistrationModel>.Builder builder,
        CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (!ImplementsIModuleRegistration(type))
                continue;

            if (HasBothStaticMethods(type))
            {
                var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fqn.StartsWith("global::"))
                    fqn = fqn.Substring("global::".Length);

                var order = GetModuleOrder(type);
                builder.Add(new ModuleRegistrationModel(fqn, order));
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            CollectModulesFromNamespace(childNs, builder, ct);
        }
    }

    private static ImmutableArray<Diagnostic> FindIncompleteModules(
        Compilation compilation, CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ct.ThrowIfCancellationRequested();
            CollectIncompleteDiagnostics(assemblySymbol.GlobalNamespace, builder, ct);
        }

        return builder.ToImmutable();
    }

    private static void CollectIncompleteDiagnostics(
        INamespaceSymbol ns,
        ImmutableArray<Diagnostic>.Builder builder,
        CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (!ImplementsIModuleRegistration(type))
                continue;

            if (HasBothStaticMethods(type))
                continue;

            var hasConfigureServices = HasStaticMethod(type, "ConfigureServices");
            var hasConfigureEndpoints = HasStaticMethod(type, "ConfigureEndpoints");

            if (!hasConfigureServices)
            {
                builder.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IncompleteModuleRegistration,
                    Location.None,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "ConfigureServices"));
            }

            if (!hasConfigureEndpoints)
            {
                builder.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IncompleteModuleRegistration,
                    Location.None,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "ConfigureEndpoints"));
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            CollectIncompleteDiagnostics(childNs, builder, ct);
        }
    }

    private static bool ImplementsIModuleRegistration(INamedTypeSymbol type)
    {
        if (type.IsAbstract || type.IsStatic)
            return false;

        foreach (var iface in type.AllInterfaces)
        {
            var ns = iface.ContainingNamespace?.ToDisplayString();
            if (iface.Name == "IModuleRegistration" &&
                ns is not null &&
                (ns == "BuildingBlocks.Infrastructure.Registration" ||
                 ns.EndsWith(".BuildingBlocks.Infrastructure.Registration")))
                return true;
        }

        return false;
    }

    private static bool HasBothStaticMethods(INamedTypeSymbol type)
    {
        return HasStaticMethod(type, "ConfigureServices") &&
               HasStaticMethod(type, "ConfigureEndpoints");
    }

    private static bool HasStaticMethod(INamedTypeSymbol type, string methodName)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol method && method.IsStatic && method.Name == methodName)
                return true;
        }

        return false;
    }

    private static int GetModuleOrder(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "ModuleOrderAttribute" &&
                attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Modulus.Mediator.Abstractions" &&
                attr.ConstructorArguments.Length == 1 &&
                attr.ConstructorArguments[0].Value is int order)
            {
                return order;
            }
        }

        return int.MaxValue;
    }

    private static void Execute(
        SourceProductionContext context,
        EquatableArray<ModuleRegistrationModel> modules,
        string? rootNamespace,
        string? assemblyName)
    {
        var ns = rootNamespace ?? assemblyName ?? "GeneratedRegistrations";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public static class GeneratedModuleRegistration");
        sb.AppendLine("{");

        // AddAllModules
        sb.AppendLine("    public static IServiceCollection AddAllModules(");
        sb.AppendLine("        this IServiceCollection services,");
        sb.AppendLine("        IConfiguration configuration)");
        sb.AppendLine("    {");

        if (modules.Length > 0)
        {
            sb.AppendLine("        // Auto-discovered modules");
            foreach (var module in modules.Array)
            {
                sb.AppendLine($"        {module.FullyQualifiedName}.ConfigureServices(services, configuration);");
            }
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // MapAllModuleEndpoints
        sb.AppendLine("    public static IEndpointRouteBuilder MapAllModuleEndpoints(");
        sb.AppendLine("        this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");

        if (modules.Length > 0)
        {
            sb.AppendLine("        // Auto-discovered modules");
            foreach (var module in modules.Array)
            {
                sb.AppendLine($"        {module.FullyQualifiedName}.ConfigureEndpoints(app);");
            }
        }

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource(
            "GeneratedModuleRegistration.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
    }
}

internal readonly struct ModuleRegistrationModel : IEquatable<ModuleRegistrationModel>
{
    public string FullyQualifiedName { get; }
    public int Order { get; }

    public ModuleRegistrationModel(string fullyQualifiedName, int order)
    {
        FullyQualifiedName = fullyQualifiedName;
        Order = order;
    }

    public bool Equals(ModuleRegistrationModel other) =>
        FullyQualifiedName == other.FullyQualifiedName && Order == other.Order;

    public override bool Equals(object obj) =>
        obj is ModuleRegistrationModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (FullyQualifiedName?.GetHashCode() ?? 0) * 397 ^ Order;
        }
    }
}

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array)
    {
        _array = array;
    }

    public ImmutableArray<T> Array => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    public int Length => Array.Length;

    public bool Equals(EquatableArray<T> other)
    {
        var left = Array;
        var right = other.Array;

        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object obj) =>
        obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var arr = Array;
        if (arr.Length == 0)
            return 0;

        unchecked
        {
            var hash = 0;
            foreach (var item in arr)
                hash = (hash * 397) ^ item.GetHashCode();
            return hash;
        }
    }
}