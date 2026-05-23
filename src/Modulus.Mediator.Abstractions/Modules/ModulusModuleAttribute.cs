namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Marks a class as a Modulus module for the source-generated <c>AddAllModules</c> /
/// <c>MapAllModuleEndpoints</c> registration. Use this when you prefer attribute discovery
/// over implementing <c>IModuleRegistration</c>; both paths require the same shape — the
/// class must expose <c>public static void ConfigureServices(IServiceCollection, IConfiguration)</c>
/// and <c>public static IEndpointRouteBuilder ConfigureEndpoints(IEndpointRouteBuilder)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModulusModuleAttribute : Attribute
{
    /// <summary>
    /// Optional ordering hint. Lower values register earlier. Equivalent to
    /// <c>[ModuleOrder(int)]</c>. Defaults to <see cref="int.MaxValue"/>.
    /// </summary>
    public int Order { get; init; } = int.MaxValue;
}
