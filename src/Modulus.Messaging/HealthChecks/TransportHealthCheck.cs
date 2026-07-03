using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.HealthChecks;

/// <summary>
/// Probes the configured <see cref="IMessageTransport"/> when it implements
/// <see cref="ITransportHealthProbe"/>; transports without a probe are reported healthy
/// with a note rather than failing readiness for lack of introspection.
/// </summary>
internal sealed class TransportHealthCheck(IMessageTransport transport) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (transport is not ITransportHealthProbe probe)
            return HealthCheckResult.Healthy("Transport exposes no health probe.");

        try
        {
            var health = await probe.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
            return health.IsHealthy
                ? HealthCheckResult.Healthy(health.Description ?? "Transport is connected.")
                : new HealthCheckResult(context.Registration.FailureStatus, health.Description ?? "Transport is not connected.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Transport probe failed.", ex);
        }
    }
}
