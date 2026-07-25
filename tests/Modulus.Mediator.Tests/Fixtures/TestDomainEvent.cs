using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Tests.Fixtures;

public record OrderPlacedEvent(int OrderId) : IDomainEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

public class OrderPlacedHandler1 : IDomainEventHandler<OrderPlacedEvent>
{
    public List<int> HandledOrderIds { get; } = [];

    public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        HandledOrderIds.Add(domainEvent.OrderId);
        return Task.CompletedTask;
    }
}

public class OrderPlacedHandler2 : IDomainEventHandler<OrderPlacedEvent>
{
    public List<int> HandledOrderIds { get; } = [];

    public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        HandledOrderIds.Add(domainEvent.OrderId);
        return Task.CompletedTask;
    }
}

public class FailingOrderPlacedHandler : IDomainEventHandler<OrderPlacedEvent>
{
    public bool WasCalled { get; private set; }

    public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        throw new InvalidOperationException("Handler failed");
    }
}

// Cancels the shared CancellationTokenSource while handling, simulating a handler whose work
// causes (or observes) cancellation partway through Publish's dispatch loop.
public class CancelingOrderPlacedHandler : IDomainEventHandler<OrderPlacedEvent>
{
    private readonly CancellationTokenSource _cancellationTokenSource;

    public CancelingOrderPlacedHandler(CancellationTokenSource cancellationTokenSource) =>
        _cancellationTokenSource = cancellationTokenSource;

    public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }
}
