namespace Modulus.Templates;

/// <summary>
/// Options for scaffolding a new command via <c>modulus add-command</c>.
/// </summary>
public sealed record CommandOptions
{
    /// <summary>
    /// The PascalCase name of the command (e.g. "CreateProduct").
    /// </summary>
    public required string CommandName { get; init; }

    /// <summary>
    /// The PascalCase name of the module (e.g. "Catalog").
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// The PascalCase name of the containing solution (e.g. "EShop").
    /// </summary>
    public required string SolutionName { get; init; }

    /// <summary>
    /// The result type T in Result&lt;T&gt;. Null means the command returns Result (void).
    /// </summary>
    public string? ResultType { get; init; }
}
