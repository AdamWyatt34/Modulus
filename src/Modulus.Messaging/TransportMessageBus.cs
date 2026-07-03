using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Serialization;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging;

/// <summary>
/// <see cref="IMessageBus"/> over the configured <see cref="IMessageTransport"/>.
/// Publishes wrap the event's own metadata (EventId, CorrelationId, OccurredOn) into the
/// envelope; sends go point-to-point to a queue named after the command type, matching the
/// previous <c>queue:{TypeName}</c> convention.
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

    public Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class
        => SendCore(command, typeof(TCommand).Name, cancellationToken);

    public Task Send<TCommand>(TCommand command, Uri destination, CancellationToken cancellationToken = default)
        where TCommand : class
    {
        if (!string.Equals(destination.Scheme, "queue", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Only 'queue:{{name}}' destinations are supported; got '{destination}'.",
                nameof(destination));
        }

        var queueName = destination.AbsolutePath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException($"Destination '{destination}' has no queue name.", nameof(destination));

        return SendCore(command, queueName, cancellationToken);
    }

    private Task SendCore<TCommand>(TCommand command, string queueName, CancellationToken cancellationToken)
        where TCommand : class
    {
        var commandType = command.GetType();

        var envelope = new TransportEnvelope(
            MessageTypeRegistry.GetStableName(commandType),
            Guid.NewGuid(),
            null,
            DateTime.UtcNow,
            MessageSerializer.Serialize(command, commandType));

        return transport.SendAsync(envelope, queueName, cancellationToken);
    }
}
