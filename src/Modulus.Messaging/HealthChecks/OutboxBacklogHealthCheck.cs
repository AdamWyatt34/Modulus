using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.HealthChecks;

/// <summary>
/// Reports outbox backlog depth: the number of unprocessed, not-yet-dead-lettered outbox rows.
/// A growing backlog means dispatch is failing or falling behind the publish rate. Hosts
/// without a registered <see cref="IOutboxStore"/> are reported healthy.
/// </summary>
internal sealed class OutboxBacklogHealthCheck(
    IServiceScopeFactory scopeFactory,
    MessagingOptions messagingOptions,
    ModulusMessagingHealthCheckOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var outboxStore = scope.ServiceProvider.GetService<IOutboxStore>();

        if (outboxStore is null)
            return HealthCheckResult.Healthy("No outbox is configured.");

        int pending;
        try
        {
            pending = await outboxStore
                .CountPending(messagingOptions.RetryPolicy.MaxAttempts, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Outbox backlog query failed.", ex);
        }

        var data = new Dictionary<string, object> { ["pending"] = pending };

        if (pending >= options.OutboxUnhealthyThreshold)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Outbox backlog is {pending} (unhealthy at {options.OutboxUnhealthyThreshold}).",
                data: data);
        }

        if (pending >= options.OutboxDegradedThreshold)
        {
            return HealthCheckResult.Degraded(
                $"Outbox backlog is {pending} (degraded at {options.OutboxDegradedThreshold}).",
                data: data);
        }

        return HealthCheckResult.Healthy($"Outbox backlog is {pending}.", data);
    }
}
