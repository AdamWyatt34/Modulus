using Microsoft.Extensions.DependencyInjection;

namespace Modulus.Messaging.HealthChecks;

/// <summary>Registers the Modulus messaging health checks.</summary>
public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds two checks tagged <c>ready</c> and <c>messaging</c>:
    /// <c>modulus_messaging_transport</c> (broker connectivity, when the transport exposes a
    /// probe) and <c>modulus_messaging_outbox</c> (backlog depth against configurable
    /// thresholds). Call after <c>AddModulusMessaging(...)</c>.
    /// </summary>
    public static IHealthChecksBuilder AddModulusMessaging(
        this IHealthChecksBuilder builder,
        Action<ModulusMessagingHealthCheckOptions>? configure = null)
    {
        var options = new ModulusMessagingHealthCheckOptions();
        configure?.Invoke(options);

        if (options.OutboxDegradedThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(configure), options.OutboxDegradedThreshold,
                "OutboxDegradedThreshold must be at least 1.");

        if (options.OutboxUnhealthyThreshold < options.OutboxDegradedThreshold)
            throw new ArgumentOutOfRangeException(nameof(configure), options.OutboxUnhealthyThreshold,
                "OutboxUnhealthyThreshold must be greater than or equal to OutboxDegradedThreshold.");

        builder.Services.AddSingleton(options);

        return builder
            .AddCheck<TransportHealthCheck>("modulus_messaging_transport", tags: ["ready", "messaging"])
            .AddCheck<OutboxBacklogHealthCheck>("modulus_messaging_outbox", tags: ["ready", "messaging"]);
    }
}
