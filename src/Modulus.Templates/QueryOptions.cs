namespace Modulus.Templates;

/// <summary>
/// Options for scaffolding a new query via <c>modulus add-query</c>.
/// </summary>
public sealed record QueryOptions
{
    /// <summary>
    /// The PascalCase name of the query (e.g. "GetProductById").
    /// </summary>
    public required string QueryName { get; init; }

    /// <summary>
    /// The PascalCase name of the module (e.g. "Catalog").
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// The PascalCase name of the containing solution (e.g. "EShop").
    /// </summary>
    public required string SolutionName { get; init; }

    /// <summary>
    /// The result type T in Result&lt;T&gt;. Always required for queries.
    /// </summary>
    public required string ResultType { get; init; }
}
