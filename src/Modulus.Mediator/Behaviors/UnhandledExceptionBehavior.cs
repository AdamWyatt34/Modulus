using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Internals;

namespace Modulus.Mediator.Behaviors;

public sealed class UnhandledExceptionBehavior<TRequest, TResponse>(
    ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            var requestName = typeof(TRequest).Name;
            logger.LogError(ex, "Unhandled exception for {RequestName}", requestName);

            return ResultFactory.CreateFailureResult<TResponse>(
                Error.Failure("UnhandledException", "An unexpected error occurred."));
        }
    }
}
