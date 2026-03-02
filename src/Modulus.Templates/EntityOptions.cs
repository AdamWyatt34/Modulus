namespace Modulus.Templates;

/// <summary>
/// Options for scaffolding a new entity via <c>modulus add-entity</c>.
/// </summary>
public sealed record EntityOptions
{
    /// <summary>
    /// The PascalCase name of the entity (e.g. "Product").
    /// </summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// The PascalCase name of the module (e.g. "Catalog").
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// The PascalCase name of the containing solution (e.g. "EShop").
    /// </summary>
    public required string SolutionName { get; init; }

    /// <summary>
    /// Whether to generate as AggregateRoot instead of Entity. Default false.
    /// </summary>
    public bool IsAggregate { get; init; }

    /// <summary>
    /// The resolved ID type: "Guid", "int", "long", "string", or a custom StronglyTypedId name.
    /// </summary>
    public string IdType { get; init; } = "Guid";

    /// <summary>
    /// Parsed property definitions. Empty if none provided.
    /// </summary>
    public IReadOnlyList<EntityProperty> Properties { get; init; } = [];
}

/// <summary>
/// A single property definition parsed from the --properties option.
/// </summary>
public sealed record EntityProperty(string Name, string Type);
