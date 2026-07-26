using Modulus.Messaging.Abstractions;

namespace Modulus.Testing.Tests.Fixtures;

/// <summary>
/// Throws on the first <paramref name="failuresBeforeSuccess"/> invocations, then succeeds.
/// <see cref="int.MaxValue"/> for practical purposes never succeeds, which is how the
/// dead-letter tests force every attempt to exhaust. The parameterless default (never fails)
/// keeps the type constructible by DI when handler discovery auto-registers every handler
/// implementing <see cref="IIntegrationEventHandler{TEvent}"/> for <see cref="TestOrderPlacedEvent"/>
/// found in this assembly — tests that need controlled failures register their own configured
/// instance alongside the harmless auto-discovered one.
/// </summary>
public sealed class FlakyOrderPlacedHandler(int failuresBeforeSuccess = 0) : IIntegrationEventHandler<TestOrderPlacedEvent>
{
    // Plain object monitor, not System.Threading.Lock: that type is net9.0+ only and this suite
    // multi-targets net8.0;net10.0.
    private readonly object _sync = new();

    public int Attempts { get; private set; }

    public List<TestOrderPlacedEvent> HandledEvents { get; } = [];

    public Task Handle(TestOrderPlacedEvent @event, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Attempts++;
            if (Attempts <= failuresBeforeSuccess)
                throw new InvalidOperationException($"Simulated failure {Attempts}");

            HandledEvents.Add(@event);
        }

        return Task.CompletedTask;
    }
}
