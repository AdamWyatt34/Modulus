using System.Diagnostics;
using System.Diagnostics.Metrics;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Behaviors;

public sealed class MetricsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private static readonly string MeterName = "Modulus.Mediator";

    private readonly Histogram<double> _handlerDuration;

    public MetricsBehavior(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _handlerDuration = meter.CreateHistogram<double>(
            "modulus.mediator.handler.duration",
            unit: "ms",
            description: "Duration of mediator handler execution in milliseconds");
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();

            _handlerDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("handler", requestName),
                new KeyValuePair<string, object?>("outcome", response.IsSuccess ? "success" : "failure"));

            return response;
        }
        catch (Exception)
        {
            stopwatch.Stop();

            _handlerDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("handler", requestName),
                new KeyValuePair<string, object?>("outcome", "exception"));

            throw;
        }
    }
}
