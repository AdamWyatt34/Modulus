using System;
using Microsoft.CodeAnalysis;

namespace Modulus.Generators;

/// <summary>
/// Pre-filters for the referenced-assembly walks in <see cref="HandlerRegistrationGenerator"/>
/// and <see cref="ModuleRegistrationGenerator"/>. Without this, both generators recursively walk
/// every namespace of every referenced assembly (the entire BCL/ASP.NET Core/EF Core/messaging
/// closure) on every compilation change, which is the dominant cost of an incremental build.
/// Handlers and modules can only be defined by assemblies that reference
/// <c>Modulus.Mediator.Abstractions</c> (directly, or transitively-made-direct the way .NET SDK
/// project references work) — every legitimate module/handler assembly satisfies this.
/// </summary>
internal static class ReferencedAssemblyFilter
{
    private const string MediatorAbstractionsAssemblyName = "Modulus.Mediator.Abstractions";

    private static readonly string[] SkippedNamePrefixes =
    {
        "System",
        "Microsoft",
        "netstandard",
        "mscorlib",
        "WindowsBase",
    };

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="assemblySymbol"/> is worth walking for
    /// Modulus handler/module types: it is not a well-known framework assembly, and it (directly
    /// or transitively) references <c>Modulus.Mediator.Abstractions</c> — the assembly that
    /// defines <c>ICommand</c>/<c>IQuery</c>/handler interfaces and <c>ModulusModuleAttribute</c>,
    /// which every real handler- or module-defining assembly must reference to compile.
    /// </summary>
    public static bool ShouldWalk(IAssemblySymbol assemblySymbol)
        => PassesNameFilter(assemblySymbol)
           && ReferencesAny(assemblySymbol, MediatorAbstractionsAssemblyName);

    /// <summary>
    /// Wider gate for <see cref="ModuleRegistrationGenerator"/>: convention-based modules
    /// (<c>*Module</c> with a static <c>ConfigureServices(IServiceCollection, IConfiguration)</c>)
    /// need no Modulus reference at all — but the <c>IServiceCollection</c> parameter forces a
    /// direct metadata reference to <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>,
    /// so that reference (or the Modulus one) marks an assembly as worth walking.
    /// </summary>
    public static bool ShouldWalkForModules(IAssemblySymbol assemblySymbol)
        => PassesNameFilter(assemblySymbol)
           && ReferencesAny(assemblySymbol, MediatorAbstractionsAssemblyName, DependencyInjectionAbstractionsAssemblyName);

    private const string DependencyInjectionAbstractionsAssemblyName = "Microsoft.Extensions.DependencyInjection.Abstractions";

    private static bool PassesNameFilter(IAssemblySymbol assemblySymbol)
    {
        var name = assemblySymbol.Name;

        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var prefix in SkippedNamePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool ReferencesAny(IAssemblySymbol assemblySymbol, params string[] assemblyNames)
    {
        if (Array.IndexOf(assemblyNames, assemblySymbol.Name) >= 0)
            return true;

        foreach (var module in assemblySymbol.Modules)
        {
            foreach (var referenced in module.ReferencedAssemblySymbols)
            {
                if (Array.IndexOf(assemblyNames, referenced.Name) >= 0)
                    return true;
            }
        }

        return false;
    }
}
