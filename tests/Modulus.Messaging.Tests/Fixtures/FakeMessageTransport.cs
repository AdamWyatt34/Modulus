using System.Collections.Concurrent;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>Records published envelopes and consumer lifecycle calls; can be told to fail publishes.</summary>
public sealed class FakeMessageTransport : IMessageTransport
{
    public ConcurrentQueue<TransportEnvelope> Published { get; } = [];

    /// <summary>When set, every publish throws this exception.</summary>
    public Exception? PublishFailure { get; set; }

    /// <summary>Number of times <see cref="StartConsumingAsync"/> was called.</summary>
    public int StartConsumingCallCount { get; private set; }

    /// <summary>Number of times <see cref="StopConsumingAsync"/> was called.</summary>
    public int StopConsumingCallCount { get; private set; }

    /// <summary>The subscriptions passed to the most recent <see cref="StartConsumingAsync"/> call.</summary>
    public IReadOnlyList<TransportSubscription>? LastSubscriptions { get; private set; }

    /// <summary>The callback passed to the most recent <see cref="StartConsumingAsync"/> call.</summary>
    public Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>>? CapturedCallback { get; private set; }

    public Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (PublishFailure is not null)
            throw PublishFailure;

        Published.Enqueue(envelope);
        return Task.CompletedTask;
    }

    public Task StartConsumingAsync(
        IReadOnlyList<TransportSubscription> subscriptions,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken cancellationToken = default)
    {
        StartConsumingCallCount++;
        LastSubscriptions = subscriptions;
        CapturedCallback = onMessage;
        return Task.CompletedTask;
    }

    public Task StopConsumingAsync(CancellationToken cancellationToken = default)
    {
        StopConsumingCallCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
