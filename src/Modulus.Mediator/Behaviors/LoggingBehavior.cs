using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        logger.LogInformation("Handling {RequestName}", requestName);

        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (response.IsSuccess)
        {
            logger.LogInformation(
                "Handled {RequestName} successfully in {ElapsedMilliseconds}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
        }
        else
        {
            var errorCodes = string.Join(", ", response.Errors.Select(e => e.Code));
            logger.LogWarning(
                "Handled {RequestName} with failure in {ElapsedMilliseconds}ms. Errors: {ErrorCodes}",
                requestName,
                stopwatch.ElapsedMilliseconds,
                errorCodes);
        }

        return response;
    }
}
