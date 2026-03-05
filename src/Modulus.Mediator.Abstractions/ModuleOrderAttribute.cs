namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Overrides the default alphabetical ordering for source-generated module registration.
/// Lower values run first. Types with equal order fall back to alphabetical by fully qualified name.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
