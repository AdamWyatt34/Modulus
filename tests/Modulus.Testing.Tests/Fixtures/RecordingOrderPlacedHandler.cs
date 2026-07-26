using Modulus.Messaging.Abstractions;

namespace Modulus.Testing.Tests.Fixtures;

/// <summary>Records every event it handles; call counts serialize with a lock since the transport may deliver concurrently.</summary>
public sealed class RecordingOrderPlacedHandler : IIntegrationEventHandler<TestOrderPlacedEvent>
{
    // Plain object monitor, not System.Threading.Lock: that type is net9.0+ only and this suite
    // multi-targets net8.0;net10.0.
    private readonly object _sync = new();

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
