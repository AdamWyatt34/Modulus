using System.Collections.Concurrent;
using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="IInboxStore"/> with the same reservation semantics as EfInboxStore:
/// <see cref="TryReserve"/> is an atomic claim (single winner under concurrency, stale
/// takeover included) and <see cref="MarkConsumerProcessed"/> completes the pair.
/// </summary>
public class FakeInboxStore : IInboxStore
{
    private sealed record ConsumerState(DateTime ReservedOnUtc, DateTime? ProcessedOnUtc);

    private readonly HashSet<Guid> _savedMessages = [];
    private readonly ConcurrentDictionary<(Guid MessageId, string HandlerName), ConsumerState> _consumers = new();

    public int SaveCalls { get; private set; }
    public List<(Guid MessageId, string HandlerName)> ProcessedConsumers { get; } = [];

    public Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        lock (_savedMessages)
        {
            _savedMessages.Add(@event.EventId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InboxMessage>> GetPending(int batchSize, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<InboxMessage>>([]);

    public Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> HasBeenProcessed(Guid messageId, string handlerName, CancellationToken cancellationToken = default)
        => Task.FromResult(
            _consumers.TryGetValue((messageId, handlerName), out var state) && state.ProcessedOnUtc is not null);

    public Task<bool> TryReserve(Guid messageId, string handlerName, TimeSpan staleAfter, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        if (_consumers.TryAdd((messageId, handlerName), new ConsumerState(now, null)))
            return Task.FromResult(true);

        // Existing entry: takeover only if unprocessed and stale, atomically (single winner).
        while (true)
        {
            if (!_consumers.TryGetValue((messageId, handlerName), out var existing))
                return Task.FromResult(false);

            if (existing.ProcessedOnUtc is not null || existing.ReservedOnUtc >= now - staleAfter)
                return Task.FromResult(false);

            if (_consumers.TryUpdate((messageId, handlerName), existing with { ReservedOnUtc = now }, existing))
                return Task.FromResult(true);
        }
    }

    public Task MarkConsumerProcessed(Guid messageId, string handlerName, CancellationToken cancellationToken = default)
    {
        _consumers.AddOrUpdate(
            (messageId, handlerName),
            _ => new ConsumerState(DateTime.UtcNow, DateTime.UtcNow),
            (_, existing) => existing with { ProcessedOnUtc = DateTime.UtcNow });

        lock (ProcessedConsumers)
        {
            ProcessedConsumers.Add((messageId, handlerName));
        }

        return Task.CompletedTask;
    }

    /// <summary>Backdates an existing reservation so tests can simulate a crashed owner.</summary>
    public void AgeReservation(Guid messageId, string handlerName, TimeSpan age)
    {
        if (_consumers.TryGetValue((messageId, handlerName), out var existing))
        {
            _consumers.TryUpdate(
                (messageId, handlerName),
                existing with { ReservedOnUtc = DateTime.UtcNow - age },
                existing);
        }
    }
}
