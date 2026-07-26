using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.InMemory;

/// <summary>
/// In-process transport backed by one unbounded <see cref="Channel{T}"/> per subscribed
/// event type. Broker semantics are mirrored deliberately: publishing a type nobody
/// subscribes to drops the message (like a fanout exchange with no bindings), and
/// dead-lettered messages are logged and dropped — there is no in-memory dead-letter queue.
/// Scheduled publishes and broker-native redelivery (<see cref="MessageDispatchResult.Retry"/>)
/// are honored with in-process timers, so <see cref="ConsumerRetryMode.Broker"/> behaves the
/// same in tests as against a real broker (minus durability).
/// </summary>
internal sealed class InMemoryTransport(
    ILogger<InMemoryTransport> logger,
    MessagingOptions? options = null) : IMessageTransport, ITransportHealthProbe
{
    private readonly ConcurrentDictionary<string, Channel<TransportEnvelope>> _channels = new(StringComparer.Ordinal);
    private readonly List<Task> _readerLoops = [];
    private CancellationTokenSource? _stopSource;

    public async Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(envelope.MessageType, out var channel))
            return;

        var delay = envelope.ScheduledEnqueueTimeUtc is { } enqueueAt
            ? enqueueAt - DateTimeOffset.UtcNow
            : TimeSpan.Zero;

        if (delay <= TimeSpan.Zero)
        {
            await channel.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Clear the schedule on the delivered copy so a redelivery doesn't re-delay it.
        EnqueueAfter(channel, envelope with { ScheduledEnqueueTimeUtc = null }, delay);
    }

    // Fire-and-forget by design: TryWrite on an unbounded channel only fails once the writer
    // completes (stop), at which point the copy is deliberately lost (no durability here).
    private static void EnqueueAfter(Channel<TransportEnvelope> channel, TransportEnvelope envelope, TimeSpan delay)
        => _ = Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            channel.Writer.TryWrite(envelope);
        });

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
                () => ReadLoop(channel, onMessage, _stopSource.Token),
                CancellationToken.None));
        }

        return Task.CompletedTask;
    }

    private async Task ReadLoop(
        Channel<TransportEnvelope> channel,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken stopToken)
    {
        try
        {
            await foreach (var envelope in channel.Reader.ReadAllAsync(stopToken).ConfigureAwait(false))
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
                    else if (result == MessageDispatchResult.Retry)
                    {
                        ScheduleRedelivery(channel, envelope);
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

    private void ScheduleRedelivery(Channel<TransportEnvelope> channel, TransportEnvelope envelope)
    {
        var attempt = RedeliveryHeaders.GetAttempt(envelope);
        var delay = RetryDelayCalculator.GetDelay(options?.ConsumerRetry ?? new RetryPolicyOptions(), attempt);
        var copy = envelope with { Headers = RedeliveryHeaders.ForRedelivery(envelope) };

        if (delay <= TimeSpan.Zero)
        {
            channel.Writer.TryWrite(copy);
            return;
        }

        EnqueueAfter(channel, copy, delay);
    }

    public async Task StopConsumingAsync(CancellationToken cancellationToken = default)
    {
        // Pending delay timers are deliberately not awaited — a 30s backoff must not hold
        // shutdown hostage. Their TryWrite no-ops against the completed writers below, so the
        // undelivered copies are simply lost, mirroring this transport's no-durability contract.
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
