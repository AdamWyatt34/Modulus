using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Internals;

namespace Modulus.Mediator.Behaviors;

public sealed class UnhandledExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger;

    public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

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
            _logger.LogError(ex, "Unhandled exception for {RequestName}", requestName);

            return ResultFactory.CreateFailureResult<TResponse>(
                Error.Failure("UnhandledException", ex.Message));
        }
    }
}
