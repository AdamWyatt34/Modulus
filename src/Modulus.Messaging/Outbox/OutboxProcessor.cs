using System.Reflection;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Outbox;

internal sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IBus bus,
    ILogger<OutboxProcessor> logger,
    MessagingOptions options) : BackgroundService
{
    private readonly Dictionary<string, Type> _allowedTypes = BuildAllowlist(options.Assemblies);

    private static Dictionary<string, Type> BuildAllowlist(IEnumerable<Assembly> assemblies)
    {
        var integrationEventType = typeof(IIntegrationEvent);
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessages(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing outbox messages");
            }

            await Task.Delay(options.OutboxPollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessPendingMessages(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        var pending = await outboxStore.GetPending(options.OutboxBatchSize, cancellationToken).ConfigureAwait(false);

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
                logger.LogError(
                    ex,
                    "Failed to publish outbox message {MessageId}",
                    message.Id);
            }
        }

        if (processedIds.Count > 0)
            await outboxStore.MarkAsProcessed(processedIds, cancellationToken).ConfigureAwait(false);
    }
}
