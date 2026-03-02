namespace Modulus.Templates;

/// <summary>
/// Options for scaffolding a new endpoint via <c>modulus add-endpoint</c>.
/// </summary>
public sealed record EndpointOptions
{
    /// <summary>
    /// The PascalCase name of the endpoint, used in .WithName() (e.g. "GetProductById").
    /// </summary>
    public required string EndpointName { get; init; }

    /// <summary>
    /// The PascalCase name of the module (e.g. "Catalog").
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// The PascalCase name of the containing solution (e.g. "EShop").
    /// </summary>
    public required string SolutionName { get; init; }

    /// <summary>
    /// The HTTP method: GET, POST, PUT, or DELETE.
    /// </summary>
    public required string HttpMethod { get; init; }

    /// <summary>
    /// The route template relative to the module group (e.g. "/{id:guid}").
    /// </summary>
    public required string Route { get; init; }

    /// <summary>
    /// The command name to wire up. Mutually exclusive with <see cref="QueryName"/>.
    /// </summary>
    public string? CommandName { get; init; }

    /// <summary>
    /// The query name to wire up. Mutually exclusive with <see cref="CommandName"/>.
    /// </summary>
    public string? QueryName { get; init; }

    /// <summary>
    /// The result type T when wiring to a query or typed command.
    /// </summary>
    public string? ResultType { get; init; }
}
