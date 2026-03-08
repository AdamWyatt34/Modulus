using MassTransit;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging;

internal sealed class MassTransitMessageBus(IBus bus) : IMessageBus
{
    public Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        return bus.Publish((object)@event, @event.GetType(), cancellationToken);
    }

    public async Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class
    {
        var endpoint = await bus.GetSendEndpoint(new Uri($"queue:{typeof(TCommand).Name}")).ConfigureAwait(false);
        await endpoint.Send(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task Send<TCommand>(TCommand command, Uri destination, CancellationToken cancellationToken = default)
        where TCommand : class
    {
        var endpoint = await bus.GetSendEndpoint(destination).ConfigureAwait(false);
        await endpoint.Send(command, cancellationToken).ConfigureAwait(false);
    }
}
