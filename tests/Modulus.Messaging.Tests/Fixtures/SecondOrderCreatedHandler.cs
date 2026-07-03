using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Tests.Fixtures;

public class SecondOrderCreatedHandler : IIntegrationEventHandler<TestOrderCreatedEvent>
{
    private readonly System.Threading.Lock _sync = new();

    public List<TestOrderCreatedEvent> HandledEvents { get; } = [];

    public Task Handle(TestOrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            HandledEvents.Add(@event);
        }

        return Task.CompletedTask;
    }
}
