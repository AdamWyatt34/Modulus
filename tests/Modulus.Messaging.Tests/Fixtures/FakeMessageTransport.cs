using System.Collections.Concurrent;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>Records published/sent envelopes; can be told to fail publishes.</summary>
public sealed class FakeMessageTransport : IMessageTransport
{
    public ConcurrentQueue<TransportEnvelope> Published { get; } = [];
    public ConcurrentQueue<(TransportEnvelope Envelope, string Queue)> Sent { get; } = [];

    /// <summary>When set, every publish throws this exception.</summary>
    public Exception? PublishFailure { get; set; }

    public Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (PublishFailure is not null)
            throw PublishFailure;

        Published.Enqueue(envelope);
        return Task.CompletedTask;
    }

    public Task SendAsync(TransportEnvelope envelope, string queueName, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((envelope, queueName));
        return Task.CompletedTask;
    }

    public Task StartConsumingAsync(
        IReadOnlyList<TransportSubscription> subscriptions,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopConsumingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
