namespace Modulus.Templates;

/// <summary>
/// Options for scaffolding a new integration event consumer via <c>modulus add-consumer</c>.
/// </summary>
public sealed record ConsumerOptions
{
    /// <summary>
    /// The PascalCase name of the integration event to handle (e.g. "OrderShipped").
    /// </summary>
    public required string EventName { get; init; }

    /// <summary>
    /// The fully-qualified namespace that declares the event, resolved from the owning
    /// module's Integration project (e.g. "EShop.Orders.Integration.IntegrationEvents").
    /// </summary>
    public required string EventNamespace { get; init; }

    /// <summary>
    /// The PascalCase name of the consuming module that hosts the handler (e.g. "Shipping").
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// The PascalCase name of the containing solution (e.g. "EShop").
    /// </summary>
    public required string SolutionName { get; init; }
}
