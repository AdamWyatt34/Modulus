using System.Diagnostics;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging;

/// <summary>
/// <see cref="IMessageBus"/> over the configured <see cref="IMessageTransport"/>.
/// Publishes wrap the event's own metadata (EventId, CorrelationId, OccurredOn) into the
/// envelope, under a producer <see cref="Activity"/> whose W3C context rides the envelope
/// headers so consumers on the other side of the broker join the same trace.
/// </summary>
internal sealed class TransportMessageBus(
    IMessageTransport transport,
    MessageTypeRegistry typeRegistry) : IMessageBus
{
    private static readonly ActivitySource Source = new(MessagingDiagnostics.ActivitySourceName);

    public Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
        => PublishCore(@event, scheduledEnqueueTimeUtc: null, cancellationToken);

    public Task PublishScheduled<TEvent>(TEvent @event, DateTimeOffset enqueueAtUtc, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
        => PublishCore(
            @event,
            // At-or-before-now schedules publish immediately rather than round-tripping a
            // zero delay through the broker's scheduling machinery.
            enqueueAtUtc <= DateTimeOffset.UtcNow ? null : enqueueAtUtc,
            cancellationToken);

    private async Task PublishCore<TEvent>(
        TEvent @event,
        DateTimeOffset? scheduledEnqueueTimeUtc,
        CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var eventType = @event.GetType();
        var messageTypeName = typeRegistry.GetName(eventType);

        using var activity = Source.StartActivity($"{messageTypeName} publish", ActivityKind.Producer);
        activity?.SetTag("modulus.message_id", @event.EventId);
        activity?.SetTag("modulus.message_type", messageTypeName);
        if (scheduledEnqueueTimeUtc is { } scheduled)
            activity?.SetTag("modulus.scheduled_enqueue_time", scheduled.UtcDateTime.ToString("O"));

        var envelope = new TransportEnvelope(
            messageTypeName,
            @event.EventId,
            @event.CorrelationId,
            @event.OccurredOn,
            MessageSerializer.Serialize(@event, eventType))
        {
            // Fall back to the ambient activity when this publish wasn't sampled, so an
            // instrumented caller still propagates its context across the broker.
            Headers = TraceContextPropagation.Inject(activity ?? Activity.Current),
            ScheduledEnqueueTimeUtc = scheduledEnqueueTimeUtc,
        };

        try
        {
            await transport.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
