using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>Throws on the first <see cref="FailuresBeforeSuccess"/> invocations, then succeeds.</summary>
public class FlakyOrderCreatedHandler(int failuresBeforeSuccess) : IIntegrationEventHandler<TestOrderCreatedEvent>
{
    public int FailuresBeforeSuccess { get; } = failuresBeforeSuccess;
    public int Attempts { get; private set; }
    public List<TestOrderCreatedEvent> HandledEvents { get; } = [];

    public Task Handle(TestOrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        Attempts++;
        if (Attempts <= FailuresBeforeSuccess)
            throw new InvalidOperationException($"Simulated failure {Attempts}");

        HandledEvents.Add(@event);
        return Task.CompletedTask;
    }
}
