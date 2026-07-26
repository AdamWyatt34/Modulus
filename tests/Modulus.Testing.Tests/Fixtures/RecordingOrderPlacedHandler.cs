using Modulus.Messaging.Abstractions;

namespace Modulus.Testing.Tests.Fixtures;

/// <summary>Records every event it handles; call counts serialize with a lock since the transport may deliver concurrently.</summary>
public sealed class RecordingOrderPlacedHandler : IIntegrationEventHandler<TestOrderPlacedEvent>
{
    private readonly System.Threading.Lock _sync = new();

    public List<TestOrderPlacedEvent> HandledEvents { get; } = [];

    public Task Handle(TestOrderPlacedEvent @event, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            HandledEvents.Add(@event);
        }

        return Task.CompletedTask;
    }
}
