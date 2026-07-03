using System.Diagnostics;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Behaviors;

/// <summary>
/// Wraps every request in a <see cref="Activity"/> from the "Modulus.Mediator" source,
/// tagging request type and outcome (success / failure with error count / exception).
/// Register an OpenTelemetry listener with <c>.AddSource("Modulus.Mediator")</c> to export.
/// </summary>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    /// <summary>The <see cref="ActivitySource"/> name to subscribe to in OpenTelemetry configuration.</summary>
    public const string ActivitySourceName = "Modulus.Mediator";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        using var activity = Source.StartActivity(typeof(TRequest).Name, ActivityKind.Internal);
        activity?.SetTag("modulus.request_type", typeof(TRequest).FullName);

        try
        {
            var response = await next();

            if (activity is not null)
            {
                if (response.IsSuccess)
                {
                    activity.SetTag("modulus.outcome", "success");
                }
                else
                {
                    var firstError = response.Errors.Count > 0 ? response.Errors[0] : Error.None;
                    activity.SetTag("modulus.outcome", "failure");
                    activity.SetTag("modulus.error_count", response.Errors.Count);
                    activity.SetTag("modulus.error_code", firstError.Code);
                    activity.SetStatus(ActivityStatusCode.Error, firstError.Description);
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetTag("modulus.outcome", "exception");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
