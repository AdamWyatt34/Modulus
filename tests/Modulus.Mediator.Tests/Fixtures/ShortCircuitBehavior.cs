using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Internals;

namespace Modulus.Mediator.Tests.Fixtures;

public class ShortCircuitBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Short-circuit: return failure without calling next()
        return Task.FromResult(
            ResultFactory.CreateFailureResult<TResponse>(
                Error.Failure("ShortCircuit", "Request was short-circuited")));
    }
}
