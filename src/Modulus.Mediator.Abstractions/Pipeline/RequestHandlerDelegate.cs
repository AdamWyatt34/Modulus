namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents the next action in the pipeline, returning a <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
/// <returns>A task producing the response.</returns>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
