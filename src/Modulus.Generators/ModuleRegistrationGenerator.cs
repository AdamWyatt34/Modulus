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
    private const string EndpointRouteBuilderFullName = "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
    private const string ServiceCollectionFullName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string ConfigurationFullName = "Microsoft.Extensions.Configuration.IConfiguration";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modulesProvider = context.CompilationProvider
            .Select(static (compilation, ct) => FindModuleRegistrations(compilation, ct));

        var isAspNetCoreHost = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName(EndpointRouteBuilderFullName) is not null);

        var rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns);
                return ns;
            });

        var assemblyName = context.CompilationProvider
            .Select(static (c, _) => c.AssemblyName);

        var namespaceInfo = rootNamespace.Combine(assemblyName);
        var combined = modulesProvider.Combine(namespaceInfo).Combine(isAspNetCoreHost);

        context.RegisterSourceOutput(combined, static (spc, data) =>
            Execute(spc, data.Left.Left, data.Left.Right.Left, data.Left.Right.Right, data.Right));

        var diagnosticsProvider = context.CompilationProvider
            .SelectMany(static (compilation, ct) => FindIncompleteModules(compilation, ct));

        context.RegisterSourceOutput(diagnosticsProvider, static (spc, diag) =>
            spc.ReportDiagnostic(diag));
    }

    private static EquatableArray<ModuleRegistrationModel> FindModuleRegistrations(
        Compilation compilation, CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<ModuleRegistrationModel>();

        // Single-assembly monoliths (a WebApi/host project that also declares its own
        // IModuleRegistration types directly) must be discovered too — not just modules that
        // live in separately-referenced projects.
        CollectModulesFromNamespace(compilation.Assembly.GlobalNamespace, builder, ct);

        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ct.ThrowIfCancellationRequested();

            // Skip the BCL/framework closure and any assembly that couldn't possibly define a
            // module — the dominant cost of this walk otherwise (H-GEN5).
            if (!ReferencedAssemblyFilter.ShouldWalkForModules(assemblySymbol))
                continue;

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
                // Keep the `global::` prefix — stripping it lets a host-side namespace segment
                // (e.g. a host type named `Orders`) shadow the module's own top-level namespace,
                // producing CS0234 in the generated call.
                var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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

        CollectIncompleteDiagnostics(compilation.Assembly.GlobalNamespace, builder, ct);

        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ct.ThrowIfCancellationRequested();

            if (!ReferencedAssemblyFilter.ShouldWalkForModules(assemblySymbol))
                continue;

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

            var hasConfigureServices = HasValidConfigureServicesMethod(type);
            var hasConfigureEndpoints = HasValidConfigureEndpointsMethod(type);

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

        // Attribute-based discovery: a class decorated with [ModulusModule] is treated as a
        // module candidate even when it does not implement IModuleRegistration. Both paths
        // require the same ConfigureServices/ConfigureEndpoints static-method shape.
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "ModulusModuleAttribute" &&
                attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Modulus.Mediator.Abstractions")
                return true;
        }

        return false;
    }

    private static bool HasBothStaticMethods(INamedTypeSymbol type) =>
        HasValidConfigureServicesMethod(type) && HasValidConfigureEndpointsMethod(type);

    private static bool HasValidConfigureServicesMethod(INamedTypeSymbol type) =>
        HasValidStaticMethod(type, "ConfigureServices", ServiceCollectionFullName, ConfigurationFullName);

    private static bool HasValidConfigureEndpointsMethod(INamedTypeSymbol type) =>
        HasValidStaticMethod(type, "ConfigureEndpoints", EndpointRouteBuilderFullName);

    /// <summary>
    /// Looks for a <c>public static</c> method with the given name and exact parameter-type
    /// signature. Discovery previously only checked name + <see cref="IMethodSymbol.IsStatic"/>,
    /// so an explicit interface implementation (private, and not callable via the type name) or a
    /// method with the wrong parameters would still be treated as "present" and generate a
    /// non-compiling call (CS0122/CS1501). Now such a method is treated as missing, so the
    /// existing MODGEN004 diagnostic fires and the module is skipped from auto-registration
    /// instead.
    /// </summary>
    private static bool HasValidStaticMethod(
        INamedTypeSymbol type,
        string methodName,
        params string[] parameterTypeFullyQualifiedNames)
    {
        foreach (var member in type.GetMembers(methodName))
        {
            if (member is not IMethodSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public } method)
                continue;

            if (method.Parameters.Length != parameterTypeFullyQualifiedNames.Length)
                continue;

            var isMatch = true;
            for (var i = 0; i < parameterTypeFullyQualifiedNames.Length; i++)
            {
                if (!ParameterTypeMatches(method.Parameters[i], parameterTypeFullyQualifiedNames[i]))
                {
                    isMatch = false;
                    break;
                }
            }

            if (isMatch)
                return true;
        }

        return false;
    }

    private static bool ParameterTypeMatches(IParameterSymbol parameter, string expectedFullyQualifiedName)
    {
        var actual = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (actual.StartsWith("global::", StringComparison.Ordinal))
            actual = actual.Substring("global::".Length);

        return actual == expectedFullyQualifiedName;
    }

    private static int GetModuleOrder(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ContainingNamespace?.ToDisplayString() != "Modulus.Mediator.Abstractions")
                continue;

            // [ModuleOrder(int)] — positional argument
            if (attr.AttributeClass.Name == "ModuleOrderAttribute" &&
                attr.ConstructorArguments.Length == 1 &&
                attr.ConstructorArguments[0].Value is int order)
            {
                return order;
            }

            // [ModulusModule(Order = int)] — named argument
            if (attr.AttributeClass.Name == "ModulusModuleAttribute")
            {
                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "Order" && named.Value.Value is int namedOrder)
                        return namedOrder;
                }
            }
        }

        return int.MaxValue;
    }

    private static void Execute(
        SourceProductionContext context,
        EquatableArray<ModuleRegistrationModel> modules,
        string? rootNamespace,
        string? assemblyName,
        bool isAspNetCoreHost)
    {
        if (!isAspNetCoreHost)
            return;

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
        sb.AppendLine($"[global::System.CodeDom.Compiler.GeneratedCode(\"Modulus.Generators.ModuleRegistrationGenerator\", \"{GeneratorVersion.Value}\")]");
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

    public bool IsEmpty => Length == 0;

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
