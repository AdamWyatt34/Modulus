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

public record RestartCycleEvent : IntegrationEvent
{
    public required int Value { get; init; }
}

public class RestartCycleHandler : IIntegrationEventHandler<RestartCycleEvent>
{
    public static ConcurrentQueue<RestartCycleEvent> Handled { get; } = [];

    public Task Handle(RestartCycleEvent @event, CancellationToken cancellationToken = default)
    {
        Handled.Enqueue(@event);
        return Task.CompletedTask;
    }
}

public record PreDeclaredTopologyEvent : IntegrationEvent
{
    public required int Value { get; init; }
}

public class PreDeclaredTopologyHandler : IIntegrationEventHandler<PreDeclaredTopologyEvent>
{
    public static ConcurrentQueue<PreDeclaredTopologyEvent> Handled { get; } = [];

    public Task Handle(PreDeclaredTopologyEvent @event, CancellationToken cancellationToken = default)
    {
        Handled.Enqueue(@event);
        return Task.CompletedTask;
    }
}

// Three distinct event types published concurrently by the H-TR1 regression test: each is a
// first-publish for its own exchange, so publishing all three (and repeats of each) in parallel
// races RabbitMqTransport's declared-exchange cache across concurrent publishes.

public record ConcurrentPublishEventA : IntegrationEvent
{
    public required int Value { get; init; }
}

public class ConcurrentPublishHandlerA : IIntegrationEventHandler<ConcurrentPublishEventA>
{
    public static ConcurrentQueue<ConcurrentPublishEventA> Handled { get; } = [];

    public Task Handle(ConcurrentPublishEventA @event, CancellationToken cancellationToken = default)
    {
        Handled.Enqueue(@event);
        return Task.CompletedTask;
    }
}

public record ConcurrentPublishEventB : IntegrationEvent
{
    public required int Value { get; init; }
}

public class ConcurrentPublishHandlerB : IIntegrationEventHandler<ConcurrentPublishEventB>
{
    public static ConcurrentQueue<ConcurrentPublishEventB> Handled { get; } = [];

    public Task Handle(ConcurrentPublishEventB @event, CancellationToken cancellationToken = default)
    {
        Handled.Enqueue(@event);
        return Task.CompletedTask;
    }
}

public record ConcurrentPublishEventC : IntegrationEvent
{
    public required int Value { get; init; }
}

public class ConcurrentPublishHandlerC : IIntegrationEventHandler<ConcurrentPublishEventC>
{
    public static ConcurrentQueue<ConcurrentPublishEventC> Handled { get; } = [];

    public Task Handle(ConcurrentPublishEventC @event, CancellationToken cancellationToken = default)
    {
        Handled.Enqueue(@event);
        return Task.CompletedTask;
    }
}

public record SlowEvent : IntegrationEvent
{
    public required int Value { get; init; }
}

/// <summary>
/// Simulates a handler still doing real work when StopConsumingAsync is called, so the H-TR3
/// drain test can assert the transport waits for it instead of abandoning it mid-flight.
/// </summary>
public class SlowHandler : IIntegrationEventHandler<SlowEvent>
{
    private static volatile bool _started;
    private static volatile bool _completed;

    public static bool Started => _started;

    public static bool Completed => _completed;

    public static void Reset()
    {
        _started = false;
        _completed = false;
    }

    public async Task Handle(SlowEvent @event, CancellationToken cancellationToken = default)
    {
        _started = true;
        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
        _completed = true;
    }
}
