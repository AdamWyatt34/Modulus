using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Tests.Fixtures;

public class TestOrderCreatedHandler : IIntegrationEventHandler<TestOrderCreatedEvent>
{
    public List<TestOrderCreatedEvent> HandledEvents { get; } = [];

    public Task Handle(TestOrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        HandledEvents.Add(@event);
        return Task.CompletedTask;
    }
}
