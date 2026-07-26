using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Tests.Fixtures;

public class SecondOrderCreatedHandler : IIntegrationEventHandler<TestOrderCreatedEvent>
{
    // Plain object monitor, not System.Threading.Lock: that type is net9.0+ only and this suite
    // multi-targets net8.0;net10.0.
    private readonly object _sync = new();

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
