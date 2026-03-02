namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Defines a behavior that wraps the handling of a request, forming a pipeline.
/// </summary>
/// <remarks>
/// In practice, <typeparamref name="TResponse"/> will be <see cref="Result"/> or <see cref="Result{TValue}"/>,
/// enabling behaviors to inspect <see cref="Result.IsSuccess"/>/<see cref="Result.IsFailure"/>
/// and short-circuit by returning a failure without calling <paramref name="next"/>.
/// </remarks>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <typeparam name="TResponse">The type of response from the handler.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Executes the behavior, optionally calling the next delegate in the pipeline.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="next">The delegate representing the next action in the pipeline.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task producing the response.</returns>
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
