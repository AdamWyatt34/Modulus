using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.InMemory;

/// <summary>
/// In-process transport backed by one unbounded <see cref="Channel{T}"/> per subscribed
/// event type. Broker semantics are mirrored deliberately: publishing a type nobody
/// subscribes to drops the message (like a fanout exchange with no bindings), and
/// dead-lettered messages are logged and dropped — there is no in-memory dead-letter queue.
/// </summary>
internal sealed class InMemoryTransport(ILogger<InMemoryTransport> logger) : IMessageTransport, ITransportHealthProbe
{
    private readonly ConcurrentDictionary<string, Channel<TransportEnvelope>> _channels = new(StringComparer.Ordinal);
    private readonly List<Task> _readerLoops = [];
    private CancellationTokenSource? _stopSource;

    public async Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (_channels.TryGetValue(envelope.MessageType, out var channel))
            await channel.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    public Task StartConsumingAsync(
        IReadOnlyList<TransportSubscription> subscriptions,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken cancellationToken = default)
    {
        _stopSource = new CancellationTokenSource();

        foreach (var subscription in subscriptions)
        {
            var channel = _channels.GetOrAdd(
                subscription.MessageTypeName,
                static _ => Channel.CreateUnbounded<TransportEnvelope>());

            _readerLoops.Add(Task.Run(
                () => ReadLoop(channel.Reader, onMessage, _stopSource.Token),
                CancellationToken.None));
        }

        return Task.CompletedTask;
    }

    private async Task ReadLoop(
        ChannelReader<TransportEnvelope> reader,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken stopToken)
    {
        try
        {
            await foreach (var envelope in reader.ReadAllAsync(stopToken).ConfigureAwait(false))
            {
                try
                {
                    var result = await onMessage(envelope, stopToken).ConfigureAwait(false);

                    if (result == MessageDispatchResult.DeadLetter)
                    {
                        logger.LogError(
                            "Dropping dead-lettered message {MessageId} of type {MessageType}: the in-memory transport has no dead-letter queue.",
                            envelope.MessageId,
                            envelope.MessageType);
                    }
                }
                catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Unexpected dispatch failure for message {MessageId} of type {MessageType}.",
                        envelope.MessageId,
                        envelope.MessageType);
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Forced stop after the drain window elapsed.
        }
    }

    public async Task StopConsumingAsync(CancellationToken cancellationToken = default)
    {
        foreach (var channel in _channels.Values)
            channel.Writer.TryComplete();

        try
        {
            // Completed writers let the reader loops drain buffered messages and exit.
            await Task.WhenAll(_readerLoops).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Drain window elapsed: force the loops down.
            if (_stopSource is not null)
                await _stopSource.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            _readerLoops.Clear();
            _channels.Clear();
            _stopSource?.Dispose();
            _stopSource = null;
        }
    }

    public ValueTask<TransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new TransportHealth(true, "In-memory transport has no broker."));

    public async ValueTask DisposeAsync() => await StopConsumingAsync().ConfigureAwait(false);
}
