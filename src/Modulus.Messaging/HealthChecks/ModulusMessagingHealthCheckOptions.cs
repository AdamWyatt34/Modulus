namespace Modulus.Messaging.HealthChecks;

/// <summary>
/// Thresholds for the Modulus messaging health checks registered by
/// <c>IHealthChecksBuilder.AddModulusMessaging()</c>.
/// </summary>
public sealed class ModulusMessagingHealthCheckOptions
{
    /// <summary>
    /// Pending outbox messages at or above this count report <c>Degraded</c> — dispatch is
    /// falling behind but the host can still serve traffic. Defaults to 100.
    /// </summary>
    public int OutboxDegradedThreshold { get; set; } = 100;

    /// <summary>
    /// Pending outbox messages at or above this count report <c>Unhealthy</c>. Defaults to 1000.
    /// </summary>
    public int OutboxUnhealthyThreshold { get; set; } = 1000;
}
