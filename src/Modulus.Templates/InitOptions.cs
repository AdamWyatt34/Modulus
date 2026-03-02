namespace Modulus.Templates;

/// <summary>
/// Options for scaffolding a new solution via <c>modulus init</c>.
/// </summary>
public sealed record InitOptions
{
    /// <summary>
    /// The PascalCase name of the solution (e.g. "EShop").
    /// </summary>
    public required string SolutionName { get; init; }

    /// <summary>
    /// Whether to include .NET Aspire orchestration projects.
    /// </summary>
    public bool IncludeAspire { get; init; }
}
