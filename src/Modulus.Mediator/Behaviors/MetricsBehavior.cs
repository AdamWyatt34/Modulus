using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Behaviors;

/// <summary>
/// Caches the "Modulus.Mediator" handler-duration <see cref="Histogram{T}"/> per
/// <see cref="IMeterFactory"/>. <see cref="MetricsBehavior{TRequest, TResponse}"/> is registered
/// transient, so without this cache every request would create a brand-new Meter/Histogram pair
/// instead of reusing the one for its (typically singleton) factory. A <see cref="ConditionalWeakTable{TKey, TValue}"/>
/// is used instead of a plain dictionary so an <see cref="IMeterFactory"/> is never kept alive by this cache.
/// </summary>
internal static class MediatorMeterCache
{
    private const string MeterName = "Modulus.Mediator";

    private static readonly ConditionalWeakTable<IMeterFactory, Histogram<double>> HandlerDurationHistograms = new();

    public static Histogram<double> GetHandlerDuration(IMeterFactory meterFactory) =>
        HandlerDurationHistograms.GetValue(meterFactory, static factory =>
            factory.Create(MeterName).CreateHistogram<double>(
                "modulus.mediator.handler.duration",
                unit: "ms",
                description: "Duration of mediator handler execution in milliseconds"));
}

public sealed class MetricsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly Histogram<double> _handlerDuration;

    public MetricsBehavior(IMeterFactory meterFactory)
    {
        _handlerDuration = MediatorMeterCache.GetHandlerDuration(meterFactory);
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
            var response = await next(cancellationToken);
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
