using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Internals;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.Outbox;

internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IMessageTransport transport,
    MessageTypeRegistry typeRegistry,
    ILogger<OutboxDispatcher> logger,
    MessagingOptions options,
    MessagingMetrics metrics) : IOutboxDispatcher
{
    /// <summary>The <see cref="ActivitySource"/> name to subscribe to in OpenTelemetry configuration.</summary>
    public const string ActivitySourceName = "Modulus.Messaging.Outbox";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    // Keyed by AssemblyQualifiedName for compatibility with rows EfOutboxStore already wrote.
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
            using var activity = Source.StartActivity("outbox.dispatch", ActivityKind.Producer);
            activity?.SetTag("modulus.message_id", message.Id);
            activity?.SetTag("modulus.event_type", message.EventType);

            try
            {
                if (!_allowedTypes.TryGetValue(message.EventType, out var eventType))
                {
                    logger.LogWarning(
                        "Outbox message {MessageId} has unknown or disallowed event type {EventType}. Skipping.",
                        message.Id,
                        message.EventType);
                    activity?.SetTag("modulus.outcome", "skipped_unknown_type");
                    metrics.OutboxMessage("skipped_unknown_type");
                    continue;
                }

                // Deserialization doubles as payload validation before the bytes go on the wire.
                var @event = JsonSerializer.Deserialize(message.Payload, eventType);
                if (@event is not IIntegrationEvent integrationEvent)
                {
                    logger.LogWarning(
                        "Failed to deserialize outbox message {MessageId}",
                        message.Id);
                    activity?.SetTag("modulus.outcome", "deserialize_failed");
                    metrics.OutboxMessage("deserialize_failed");
                    continue;
                }

                var envelope = new TransportEnvelope(
                    typeRegistry.GetName(eventType),
                    integrationEvent.EventId,
                    integrationEvent.CorrelationId,
                    integrationEvent.OccurredOn,
                    Encoding.UTF8.GetBytes(message.Payload));

                await transport.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
                processedIds.Add(message.Id);
                activity?.SetTag("modulus.outcome", "published");
                metrics.OutboxMessage("published");
            }
            catch (Exception ex)
            {
                var nextAttempt = message.Attempts + 1;
                var outcome = nextAttempt >= maxAttempts ? "dead_lettered" : "retry_pending";
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("modulus.outcome", outcome);
                activity?.SetTag("modulus.attempt", nextAttempt);
                metrics.OutboxMessage(outcome);

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
