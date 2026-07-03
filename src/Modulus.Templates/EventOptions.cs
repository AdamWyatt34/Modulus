namespace Modulus.Templates;

/// <summary>
/// Options for scaffolding a new integration event via <c>modulus add-event</c>.
/// </summary>
public sealed record EventOptions
{
    /// <summary>
    /// The PascalCase name of the integration event (e.g. "OrderShipped").
    /// </summary>
    public required string EventName { get; init; }

    /// <summary>
    /// The PascalCase name of the module that owns the event (e.g. "Orders").
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// The PascalCase name of the containing solution (e.g. "EShop").
    /// </summary>
    public required string SolutionName { get; init; }

    /// <summary>
    /// Parsed property definitions that become positional record parameters. Empty if none provided.
    /// </summary>
    public IReadOnlyList<EntityProperty> Properties { get; init; } = [];
}
