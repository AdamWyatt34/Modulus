using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>
/// Handler with a configurable delay, for racing concurrent deliveries against each other.
/// The parameterless default (zero delay) keeps it constructible by handler auto-discovery.
/// </summary>
public class SlowOrderCreatedHandler(TimeSpan? delay = null) : IIntegrationEventHandler<TestOrderCreatedEvent>
{
    private readonly System.Threading.Lock _sync = new();

    public List<TestOrderCreatedEvent> HandledEvents { get; } = [];

    public async Task Handle(TestOrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        if (delay is { } d && d > TimeSpan.Zero)
            await Task.Delay(d, cancellationToken);

        lock (_sync)
        {
            HandledEvents.Add(@event);
        }
    }
}
