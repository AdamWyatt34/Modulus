namespace Modulus.Templates;

/// <summary>
/// Options for scaffolding a new module via <c>modulus add module</c>.
/// </summary>
public sealed record ModuleOptions
{
    /// <summary>
    /// The PascalCase name of the module (e.g. "Catalog").
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// The PascalCase name of the containing solution (e.g. "EShop").
    /// </summary>
    public required string SolutionName { get; init; }
}
