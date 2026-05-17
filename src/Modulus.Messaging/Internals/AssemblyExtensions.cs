using System.Reflection;

namespace Modulus.Messaging.Internals;

/// <summary>
/// Defensive helpers for reflecting over assemblies that may have missing references
/// or be dynamically generated.
/// </summary>
internal static class AssemblyExtensions
{
    /// <summary>
    /// Returns the types defined in the assembly, gracefully handling
    /// <see cref="ReflectionTypeLoadException"/> and dynamic assemblies.
    /// </summary>
    public static IReadOnlyList<Type> GetTypesSafe(this Assembly assembly)
    {
        if (assembly.IsDynamic)
            return [];

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types
                .Where(t => t is not null)
                .Cast<Type>()
                .ToArray();
        }
    }
}
