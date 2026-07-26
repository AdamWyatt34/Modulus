namespace Modulus.Mediator;

/// <summary>
/// Options for configuring the Modulus mediator, set via the <c>configure</c> delegate on
/// <see cref="ServiceCollectionExtensions.AddModulusMediator"/>.
/// </summary>
public sealed class MediatorOptions
{
    /// <summary>
    /// The strategy used to dispatch domain event handlers when
    /// <see cref="Modulus.Mediator.Abstractions.IMediator.Publish{TEvent}"/> is called.
    /// Defaults to <see cref="Modulus.Mediator.PublishStrategy.Sequential"/> — the mediator's
    /// behavior prior to 4.0.
    /// </summary>
    public PublishStrategy PublishStrategy { get; set; } = PublishStrategy.Sequential;
}
