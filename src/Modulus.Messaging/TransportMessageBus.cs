using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging;

/// <summary>
/// <see cref="IMessageBus"/> over the configured <see cref="IMessageTransport"/>.
/// Publishes wrap the event's own metadata (EventId, CorrelationId, OccurredOn) into the
/// envelope.
/// </summary>
internal sealed class TransportMessageBus(
    IMessageTransport transport,
    MessageTypeRegistry typeRegistry) : IMessageBus
{
    public Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        var eventType = @event.GetType();

        var envelope = new TransportEnvelope(
            typeRegistry.GetName(eventType),
            @event.EventId,
            @event.CorrelationId,
            @event.OccurredOn,
            MessageSerializer.Serialize(@event, eventType));

        return transport.PublishAsync(envelope, cancellationToken);
    }
}
