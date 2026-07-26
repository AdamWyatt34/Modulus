using System.Diagnostics;

namespace Modulus.Messaging.Diagnostics;

/// <summary>
/// W3C trace-context injection/extraction over the envelope's string headers, so a consumer's
/// Activity can join the producer's trace across the broker hop. Kept manual (rather than
/// <see cref="DistributedContextPropagator"/>) for a deterministic wire format: .NET activities
/// have used the W3C id format by default since .NET 5, and <see cref="Activity.Id"/> is
/// exactly the <c>traceparent</c> value.
/// </summary>
internal static class TraceContextPropagation
{
    /// <summary>
    /// Returns <paramref name="headers"/> (or a new dictionary) with the activity's
    /// <c>traceparent</c>/<c>tracestate</c> written in, or the input unchanged when
    /// <paramref name="activity"/> is <see langword="null"/> (no listener sampled it).
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Inject(
        Activity? activity,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (activity?.Id is not { } traceParent)
            return headers;

        var merged = headers is null
            ? new Dictionary<string, string>(capacity: 2, StringComparer.Ordinal)
            : new Dictionary<string, string>(headers, StringComparer.Ordinal);

        merged[MessagingDiagnostics.TraceParentHeader] = traceParent;

        if (!string.IsNullOrEmpty(activity.TraceStateString))
            merged[MessagingDiagnostics.TraceStateHeader] = activity.TraceStateString;

        return merged;
    }

    /// <summary>
    /// Parses the remote <see cref="ActivityContext"/> out of envelope headers. Returns
    /// <see langword="false"/> when no (valid) <c>traceparent</c> is present.
    /// </summary>
    public static bool TryExtract(IReadOnlyDictionary<string, string>? headers, out ActivityContext context)
    {
        context = default;

        if (headers is null || !headers.TryGetValue(MessagingDiagnostics.TraceParentHeader, out var traceParent))
            return false;

        headers.TryGetValue(MessagingDiagnostics.TraceStateHeader, out var traceState);
        return ActivityContext.TryParse(traceParent, traceState, isRemote: true, out context);
    }
}
