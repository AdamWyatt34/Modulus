using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("Handling {RequestName}", requestName);

        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (response.IsSuccess)
        {
            _logger.LogInformation(
                "Handled {RequestName} successfully in {ElapsedMilliseconds}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
        }
        else
        {
            var errorCodes = string.Join(", ", response.Errors.Select(e => e.Code));
            _logger.LogWarning(
                "Handled {RequestName} with failure in {ElapsedMilliseconds}ms. Errors: {ErrorCodes}",
                requestName,
                stopwatch.ElapsedMilliseconds,
                errorCodes);
        }

        return response;
    }
}
