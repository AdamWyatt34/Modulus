using System.Globalization;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.Dispatch;

/// <summary>
/// The envelope headers that carry broker-native redelivery state, shared between the
/// consumer pipeline (which decides <see cref="MessageDispatchResult.Retry"/>) and the
/// transports (which build the delayed copy). Internal by design: custom transports without
/// access fall back to in-process retry, which stays the default mode.
/// </summary>
internal static class RedeliveryHeaders
{
    /// <summary>1-based delivery attempt; absent means first delivery.</summary>
    public const string AttemptHeader = "modulus-delivery-attempt";

    /// <summary>
    /// The endpoint a redelivered copy is intended for. Set by transports whose redelivery
    /// mechanism fans out to every endpoint (Azure Service Bus topics); other endpoints
    /// acknowledge the copy without dispatching.
    /// </summary>
    public const string TargetEndpointHeader = "modulus-redeliver-endpoint";

    public static int GetAttempt(TransportEnvelope envelope)
        => envelope.Headers is { } headers
            && headers.TryGetValue(AttemptHeader, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempt)
            && attempt >= 1
                ? attempt
                : 1;

    /// <summary>
    /// Builds the headers for the delayed copy: the incremented attempt, optionally the
    /// target endpoint, with every other header (trace context included) carried over.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForRedelivery(
        TransportEnvelope envelope,
        string? targetEndpoint = null)
    {
        var headers = envelope.Headers is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal);

        headers[AttemptHeader] = (GetAttempt(envelope) + 1).ToString(CultureInfo.InvariantCulture);

        if (targetEndpoint is not null)
            headers[TargetEndpointHeader] = targetEndpoint;

        return headers;
    }

    /// <summary>
    /// Whether this delivery is another endpoint's redelivered copy (fan-out transports) and
    /// must be acknowledged without dispatching.
    /// </summary>
    public static bool IsForeignRedelivery(TransportEnvelope envelope, string endpointName)
        => envelope.Headers is { } headers
            && headers.TryGetValue(TargetEndpointHeader, out var target)
            && !string.Equals(target, endpointName, StringComparison.Ordinal);
}
