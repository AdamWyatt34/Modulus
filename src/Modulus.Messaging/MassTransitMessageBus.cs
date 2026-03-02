using MassTransit;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging;

internal sealed class MassTransitMessageBus : IMessageBus
{
    private readonly IBus _bus;

    public MassTransitMessageBus(IBus bus)
    {
        _bus = bus;
    }

    public Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        return _bus.Publish((object)@event, @event.GetType(), cancellationToken);
    }

    public async Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class
    {
        var endpoint = await _bus.GetSendEndpoint(new Uri($"queue:{typeof(TCommand).Name}"));
        await endpoint.Send(command, cancellationToken);
    }

    public async Task Send<TCommand>(TCommand command, Uri destination, CancellationToken cancellationToken = default)
        where TCommand : class
    {
        var endpoint = await _bus.GetSendEndpoint(destination);
        await endpoint.Send(command, cancellationToken);
    }
}
