using System.Collections.Concurrent;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.RabbitMq.IntegrationTests.Fixtures;

// One event type per scenario: handler discovery auto-registers every handler in this
// assembly, so scenarios must not share an event type or a failing handler would block
// an unrelated test's dispatch. Handlers record to static state because the consuming
// instances are created by the container, not the test.

public record RoundTripEvent : IntegrationEvent
{
    public required int Value { get; init; }
}

public class RoundTripHandler : IIntegrationEventHandler<RoundTripEvent>
{
    public static ConcurrentQueue<RoundTripEvent> Handled { get; } = [];

    public Task Handle(RoundTripEvent @event, CancellationToken cancellationToken = default)
    {
        Handled.Enqueue(@event);
        return Task.CompletedTask;
    }
}

public record DeadLetterEvent : IntegrationEvent
{
    public required int Value { get; init; }
}

/// <summary>Always throws, so every delivery exhausts ConsumerRetry and dead-letters.</summary>
public class DeadLetterHandler : IIntegrationEventHandler<DeadLetterEvent>
{
    private static int _attempts;

    public static int Attempts => _attempts;

    public static void Reset() => Interlocked.Exchange(ref _attempts, 0);

    public Task Handle(DeadLetterEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _attempts);
        throw new InvalidOperationException("Simulated permanent failure");
    }
}

public record InboxDedupEvent : IntegrationEvent
{
    public required int Value { get; init; }
}

public class InboxDedupHandler : IIntegrationEventHandler<InboxDedupEvent>
{
    public static ConcurrentQueue<InboxDedupEvent> Handled { get; } = [];

    public Task Handle(InboxDedupEvent @event, CancellationToken cancellationToken = default)
    {
        Handled.Enqueue(@event);
        return Task.CompletedTask;
    }
}
