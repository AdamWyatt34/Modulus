using Modulus.Messaging.Abstractions;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>In-memory <see cref="IInboxStore"/> with the same idempotency semantics as EfInboxStore.</summary>
public class FakeInboxStore : IInboxStore
{
    private readonly HashSet<Guid> _savedMessages = [];
    private readonly HashSet<(Guid MessageId, string HandlerName)> _consumers = [];

    public int SaveCalls { get; private set; }
    public List<(Guid MessageId, string HandlerName)> RecordedConsumers { get; } = [];

    public Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        _savedMessages.Add(@event.EventId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InboxMessage>> GetPending(int batchSize, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<InboxMessage>>([]);

    public Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> HasBeenProcessed(Guid messageId, string handlerName, CancellationToken cancellationToken = default)
        => Task.FromResult(_consumers.Contains((messageId, handlerName)));

    public Task RecordConsumer(Guid messageId, string handlerName, CancellationToken cancellationToken = default)
    {
        _consumers.Add((messageId, handlerName));
        RecordedConsumers.Add((messageId, handlerName));
        return Task.CompletedTask;
    }
}
