using System.Reflection;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Internals;

namespace Modulus.Messaging.Outbox;

internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IBus bus,
    ILogger<OutboxDispatcher> logger,
    MessagingOptions options) : IOutboxDispatcher
{
    private readonly Dictionary<string, Type> _allowedTypes = BuildAllowlist(options.Assemblies);

    private static Dictionary<string, Type> BuildAllowlist(IEnumerable<Assembly> assemblies)
    {
        var integrationEventType = typeof(IIntegrationEvent);
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypesSafe())
            {
                if (type is { IsAbstract: false, IsInterface: false }
                    && integrationEventType.IsAssignableFrom(type))
                {
                    var assemblyQualifiedName = type.AssemblyQualifiedName;
                    if (assemblyQualifiedName is not null)
                        map.TryAdd(assemblyQualifiedName, type);
                }
            }
        }

        return map;
    }

    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        var maxAttempts = options.RetryPolicy.MaxAttempts;
        var pending = await outboxStore
            .GetPending(options.OutboxBatchSize, maxAttempts, cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
            return;

        var processedIds = new List<Guid>();

        foreach (var message in pending)
        {
            try
            {
                if (!_allowedTypes.TryGetValue(message.EventType, out var eventType))
                {
                    logger.LogWarning(
                        "Outbox message {MessageId} has unknown or disallowed event type {EventType}. Skipping.",
                        message.Id,
                        message.EventType);
                    continue;
                }

                var @event = JsonSerializer.Deserialize(message.Payload, eventType);
                if (@event is null)
                {
                    logger.LogWarning(
                        "Failed to deserialize outbox message {MessageId}",
                        message.Id);
                    continue;
                }

                await bus.Publish(@event, eventType, cancellationToken).ConfigureAwait(false);
                processedIds.Add(message.Id);
            }
            catch (Exception ex)
            {
                var nextAttempt = message.Attempts + 1;

                if (nextAttempt >= maxAttempts)
                {
                    logger.LogCritical(
                        ex,
                        "Outbox message {MessageId} failed after {Attempts} attempts and is being dead-lettered",
                        message.Id,
                        nextAttempt);
                }
                else
                {
                    logger.LogError(
                        ex,
                        "Failed to publish outbox message {MessageId} (attempt {Attempt} of {Max})",
                        message.Id,
                        nextAttempt,
                        maxAttempts);
                }

                await outboxStore.MarkAsFailed(message.Id, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        if (processedIds.Count > 0)
            await outboxStore.MarkAsProcessed(processedIds, cancellationToken).ConfigureAwait(false);
    }
}
