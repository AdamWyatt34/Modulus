using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Internals;
using Modulus.Messaging.Transports;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Modulus.Messaging.RabbitMq;

/// <summary>
/// RabbitMQ transport built directly on RabbitMQ.Client. Topology: durable fanout exchange
/// per event type; one durable queue per endpoint bound to every subscribed exchange, with a
/// per-endpoint dead-letter exchange and queue. Publishes use publisher confirmations, so a
/// failed broker confirm surfaces as an exception the outbox turns into a retry.
/// <see cref="StopConsumingAsync"/> drains in-flight dispatches before returning, and
/// <see cref="CheckHealthAsync"/> reports unhealthy once consumption has started if the consume
/// channel or consumer dies unexpectedly (queue deletion, a broker-side timeout close, etc.).
/// </summary>
internal sealed class RabbitMqTransport(
    MessagingOptions options,
    ILogger<RabbitMqTransport> logger) : IMessageTransport, ITransportHealthProbe
{
    /// <summary>Bounded wait for in-flight dispatches to finish once consumption is cancelled.</summary>
    private static readonly TimeSpan ConsumerDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _declaredExchanges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _declaredScheduledQueues = new(StringComparer.Ordinal);

    // Captured by StartConsumingAsync for the broker-native redelivery path.
    private volatile string? _consumeQueue;

    private IConnection? _connection;
    private IChannel? _publishChannel;
    private volatile IChannel? _consumeChannel;
    private string? _consumerTag;

    // In-flight consumer dispatch tracking so StopConsumingAsync can drain before returning
    // (see DrainInFlightDispatchesAsync), and consumer-health state so a dead consume
    // channel/consumer is visible to CheckHealthAsync instead of reporting healthy forever.
    private long _inFlightDispatches;
    private volatile TaskCompletionSource? _drainSignal;
    private volatile bool _consumingStarted;
    private volatile bool _stoppingConsumer;
    private volatile ConsumerFault? _consumerFault;

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;

            var factory = new ConnectionFactory
            {
                Uri = new Uri(options.ConnectionString!),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
            };

            var previousConnection = _connection;
            var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            _connection = connection;

            if (previousConnection is not null)
            {
                // The prior connection is known dead here (the IsOpen fast-path above returns
                // early while it's still open), so dispose it once the new one is safely wired
                // in. Otherwise its auto-recovery timer and socket are orphaned for the rest of
                // the process's lifetime every time the broker connection drops and reconnects.
                try
                {
                    await previousConnection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to dispose the previous RabbitMQ connection while reconnecting.");
                }
            }

            return connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken)
    {
        if (_publishChannel is { IsOpen: true })
            return _publishChannel;

        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_publishChannel is { IsOpen: true })
                return _publishChannel;

            // Confirmation tracking makes BasicPublishAsync complete only on broker confirm.
            _publishChannel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                cancellationToken).ConfigureAwait(false);

            return _publishChannel;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var channel = await GetPublishChannelAsync(cancellationToken).ConfigureAwait(false);
        var exchange = RabbitMqTopology.ExchangeName(envelope.MessageType);

        if (options.AutoProvision && !_declaredExchanges.ContainsKey(exchange))
        {
            await channel.ExchangeDeclareAsync(
                exchange, ExchangeType.Fanout, durable: true, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _declaredExchanges.TryAdd(exchange, 0);
        }

        // Scheduled publish: park the message in the event type's TTL holding queue, whose
        // dead-letter exchange is the event's fanout exchange — on expiry the broker routes
        // it to subscribers exactly as an immediate publish would have.
        var delay = envelope.ScheduledEnqueueTimeUtc is { } enqueueAt
            ? enqueueAt - DateTimeOffset.UtcNow
            : TimeSpan.Zero;

        var properties = RabbitMqEnvelopeMapper.ToBasicProperties(envelope);
        var targetExchange = exchange;
        var routingKey = string.Empty;

        if (delay > TimeSpan.Zero)
        {
            var scheduledQueue = RabbitMqTopology.ScheduledQueueName(envelope.MessageType);

            if (options.AutoProvision && !_declaredScheduledQueues.ContainsKey(scheduledQueue))
            {
                await channel.QueueDeclareAsync(
                    scheduledQueue, durable: true, exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        ["x-dead-letter-exchange"] = exchange,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                _declaredScheduledQueues.TryAdd(scheduledQueue, 0);
            }

            properties.Expiration = Math.Max(1L, (long)delay.TotalMilliseconds)
                .ToString(CultureInfo.InvariantCulture);
            targetExchange = string.Empty;
            routingKey = scheduledQueue;
        }

        try
        {
            // Scheduled publishes go through the default exchange to a specific queue, so an
            // unroutable publish (queue missing under AutoProvision=false) must fault the
            // confirm via mandatory rather than be silently discarded-and-confirmed. Normal
            // event publishes stay non-mandatory: a fanout exchange with no bindings yet
            // (no subscriber has started) deliberately drops, as documented.
            await channel.BasicPublishAsync(
                targetExchange,
                routingKey,
                mandatory: delay > TimeSpan.Zero,
                basicProperties: properties,
                body: envelope.Body,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A deleted-out-of-band exchange surfaces here as a channel-level protocol
            // exception; this client gives no cheap way to distinguish that from other publish
            // failures, so evict unconditionally. The cost is one redundant, idempotent
            // re-declare on the next attempt instead of failing against this exchange forever.
            _declaredExchanges.TryRemove(exchange, out _);
            _declaredScheduledQueues.TryRemove(RabbitMqTopology.ScheduledQueueName(envelope.MessageType), out _);
            throw;
        }
    }

    public async Task StartConsumingAsync(
        IReadOnlyList<TransportSubscription> subscriptions,
        Func<TransportEnvelope, CancellationToken, Task<MessageDispatchResult>> onMessage,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var endpointName = EndpointNameResolver.Resolve(options);
        var queue = RabbitMqTopology.QueueName(endpointName);
        var deadLetterExchange = RabbitMqTopology.DeadLetterExchangeName(endpointName);

        _stoppingConsumer = false;

        var consumeChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: (ushort)Math.Clamp(options.PrefetchCount, 1, ushort.MaxValue)),
            cancellationToken).ConfigureAwait(false);

        var previousConsumeChannel = _consumeChannel;
        _consumeChannel = consumeChannel;
        _consumerFault = null;

        if (previousConsumeChannel is { IsOpen: true })
        {
            // A restart cycle (Stop then Start on the same transport instance): the previous
            // channel was only cancelled, not closed, by StopConsumingAsync, so it must be
            // disposed here explicitly or it leaks for the rest of the process's lifetime.
            try
            {
                await previousConsumeChannel.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "Failed to dispose the previous RabbitMQ consume channel while restarting consumption.");
            }
        }

        if (options.AutoProvision)
        {
            await consumeChannel.ExchangeDeclareAsync(
                deadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await consumeChannel.QueueDeclareAsync(
                RabbitMqTopology.DeadLetterQueueName(endpointName),
                durable: true, exclusive: false, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await consumeChannel.QueueBindAsync(
                RabbitMqTopology.DeadLetterQueueName(endpointName), deadLetterExchange, routingKey: string.Empty,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await consumeChannel.QueueDeclareAsync(
                queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = deadLetterExchange,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (options.ConsumerRetryMode == ConsumerRetryMode.Broker)
            {
                // Broker-native retry parking lot: expired messages dead-letter through the
                // default exchange straight back into the work queue (routing key = queue).
                await consumeChannel.QueueDeclareAsync(
                    RabbitMqTopology.RetryQueueName(endpointName),
                    durable: true, exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        ["x-dead-letter-exchange"] = string.Empty,
                        ["x-dead-letter-routing-key"] = queue,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            foreach (var subscription in subscriptions)
            {
                var exchange = RabbitMqTopology.ExchangeName(subscription.MessageTypeName);

                await consumeChannel.ExchangeDeclareAsync(
                    exchange, ExchangeType.Fanout, durable: true, autoDelete: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await consumeChannel.QueueBindAsync(
                    queue, exchange, routingKey: string.Empty,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        await consumeChannel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)Math.Clamp(options.PrefetchCount, 1, ushort.MaxValue),
            global: false,
            cancellationToken).ConfigureAwait(false);

        // Channel-level errors (consumer_timeout exceeded by an in-process retry cycle, a bad
        // ack, etc.) are not connection-level and are not auto-recovered by the client, so a
        // dead channel would otherwise go unnoticed until the next publish. The staleness check
        // guards against this firing for a channel a later Start/restart has already replaced
        // (e.g. the disposal above, on a restart cycle).
        consumeChannel.ChannelShutdownAsync += (_, args) =>
        {
            if (!ReferenceEquals(_consumeChannel, consumeChannel))
                return Task.CompletedTask;

            RecordConsumerFault($"RabbitMQ consume channel closed: {args.ReplyText} (code {args.ReplyCode}).");
            return Task.CompletedTask;
        };

        _consumeQueue = queue;

        var consumer = new AsyncEventingBasicConsumer(consumeChannel);

        consumer.ReceivedAsync += async (_, delivery) =>
        {
            Interlocked.Increment(ref _inFlightDispatches);
            try
            {
                var envelope = RabbitMqEnvelopeMapper.ToEnvelope(delivery.BasicProperties, delivery.Body);
                var result = await onMessage(envelope, delivery.CancellationToken).ConfigureAwait(false);

                if (result == MessageDispatchResult.Retry)
                {
                    // Publish the delayed copy before consuming the original; a scheduling
                    // failure downgrades to an immediate requeue so no attempt is lost.
                    try
                    {
                        await ScheduleRedeliveryAsync(envelope, delivery.CancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Failed to schedule broker redelivery for message {MessageId}; requeueing after an in-process backoff instead.",
                            envelope.MessageId);

                        // Requeue keeps the attempt header unchanged, so without a wait a
                        // persistently failing schedule path (e.g. dead publish channel)
                        // would hot-loop handler executions. Sleeping the configured backoff
                        // in process mirrors InProcess-mode semantics for this edge.
                        var backoff = RetryDelayCalculator.GetDelay(
                            options.ConsumerRetry, RedeliveryHeaders.GetAttempt(envelope));
                        if (backoff > TimeSpan.Zero)
                            await Task.Delay(backoff, delivery.CancellationToken).ConfigureAwait(false);

                        await consumeChannel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, delivery.CancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }
                }

                // Ack/nack via the channel that delivered this message (captured above), not a
                // mutable field: a stop/restart cycle can otherwise ack against a channel this
                // delivery never arrived on, which the broker closes with a 406.
                if (result is MessageDispatchResult.Acknowledge or MessageDispatchResult.Retry)
                {
                    await consumeChannel.BasicAckAsync(delivery.DeliveryTag, multiple: false, delivery.CancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    // requeue: false routes through the queue's dead-letter exchange.
                    await consumeChannel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, delivery.CancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Unexpected failure processing RabbitMQ delivery {DeliveryTag}; message will be redelivered.",
                    delivery.DeliveryTag);
            }
            finally
            {
                if (Interlocked.Decrement(ref _inFlightDispatches) == 0)
                    _drainSignal?.TrySetResult();
            }
        };

        // Fires both for a broker-initiated cancel (e.g. the queue was deleted) and for our own
        // deliberate cancel in StopConsumingAsync — _stoppingConsumer distinguishes the two so a
        // graceful stop isn't recorded as a fault.
        consumer.UnregisteredAsync += (_, _) =>
        {
            if (_stoppingConsumer || !ReferenceEquals(_consumeChannel, consumeChannel))
                return Task.CompletedTask;

            RecordConsumerFault("RabbitMQ consumer was cancelled by the broker (e.g. the queue was deleted).");
            return Task.CompletedTask;
        };

        _consumerTag = await consumeChannel.BasicConsumeAsync(
            queue, autoAck: false, consumer, cancellationToken).ConfigureAwait(false);

        // Only flip to true once consumption has actually started successfully: an exception
        // anywhere above (topology declare, QoS, BasicConsumeAsync) must not make CheckHealthAsync
        // start inspecting consumer state for a consumer that never came up.
        _consumingStarted = true;
    }

    private void RecordConsumerFault(string reason)
    {
        _consumerFault = new ConsumerFault(DateTimeOffset.UtcNow, reason);
        logger.LogWarning("RabbitMQ consumer fault recorded: {Reason}", reason);
    }

    /// <summary>
    /// Publishes the failed message's delayed copy into the endpoint's retry queue: the
    /// incremented attempt rides the headers, the backoff rides the per-message TTL, and on
    /// expiry the broker routes the copy straight back into the work queue. Uses the
    /// confirming publish channel so a lost copy surfaces as an exception (the caller then
    /// requeues the original instead of acking).
    /// </summary>
    private async Task ScheduleRedeliveryAsync(TransportEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_consumeQueue is null)
            throw new InvalidOperationException("Consumption has not started.");

        var retryQueue = RabbitMqTopology.RetryQueueName(EndpointNameResolver.Resolve(options));

        var attempt = RedeliveryHeaders.GetAttempt(envelope);
        var delay = RetryDelayCalculator.GetDelay(options.ConsumerRetry, attempt);
        var copy = envelope with { Headers = RedeliveryHeaders.ForRedelivery(envelope) };

        var properties = RabbitMqEnvelopeMapper.ToBasicProperties(copy);
        properties.Expiration = Math.Max(1L, (long)delay.TotalMilliseconds)
            .ToString(CultureInfo.InvariantCulture);

        var channel = await GetPublishChannelAsync(cancellationToken).ConfigureAwait(false);
        // Mandatory: a missing retry queue (AutoProvision=false without the documented
        // pre-created topology) must fault this publish so the caller requeues the original,
        // instead of the broker confirming a silently discarded copy while we ack the original.
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: retryQueue,
            mandatory: true,
            basicProperties: properties,
            body: copy.Body,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // A real connectivity probe: establishes the connection if it doesn't exist yet,
        // so an unreachable broker surfaces before the first publish.
        try
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (!connection.IsOpen)
                return new TransportHealth(false, "RabbitMQ connection is closed.");

            if (_consumingStarted)
            {
                var fault = _consumerFault;
                if (fault is not null)
                {
                    return new TransportHealth(
                        false,
                        $"RabbitMQ consumer stopped receiving at {fault.OccurredAtUtc:O}: {fault.Reason}");
                }

                if (_consumeChannel is not { IsOpen: true })
                    return new TransportHealth(false, "RabbitMQ consume channel is closed.");
            }

            return new TransportHealth(true, "RabbitMQ connection is open.");
        }
        catch (Exception ex)
        {
            return new TransportHealth(false, $"RabbitMQ connection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancels the consumer, then awaits any dispatches already in flight (each possibly
    /// mid-retry-sleep) so the host doesn't dispose the ServiceProvider out from under a running
    /// handler — the drain <see cref="IMessageTransport.StopConsumingAsync"/> promises.
    /// </summary>
    public async Task StopConsumingAsync(CancellationToken cancellationToken = default)
    {
        if (_consumeChannel is { IsOpen: true } channel && _consumerTag is not null)
        {
            _stoppingConsumer = true;
            await channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken).ConfigureAwait(false);
            _consumerTag = null;
        }

        await DrainInFlightDispatchesAsync(cancellationToken).ConfigureAwait(false);
        _consumingStarted = false;
    }

    /// <summary>
    /// Waits for <see cref="_inFlightDispatches"/> to reach zero, bounded by
    /// <see cref="ConsumerDrainTimeout"/> so a stuck handler can't hang shutdown forever.
    /// </summary>
    private async Task DrainInFlightDispatchesAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Read(ref _inFlightDispatches) == 0)
            return;

        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _drainSignal = signal;

        // Re-check after publishing the signal: a dispatch may have already reached zero between
        // the check above and the write, in which case no decrement would ever observe it.
        if (Interlocked.Read(ref _inFlightDispatches) == 0)
            signal.TrySetResult();

        try
        {
            await signal.Task.WaitAsync(ConsumerDrainTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogWarning(
                "Timed out after {Seconds}s waiting for {Count} in-flight RabbitMQ dispatch(es) to drain during shutdown.",
                ConsumerDrainTimeout.TotalSeconds,
                Interlocked.Read(ref _inFlightDispatches));
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Shutdown cancelled while waiting for {Count} in-flight RabbitMQ dispatch(es) to drain.",
                Interlocked.Read(ref _inFlightDispatches));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_consumeChannel is not null)
            await _consumeChannel.DisposeAsync().ConfigureAwait(false);

        if (_publishChannel is not null)
            await _publishChannel.DisposeAsync().ConfigureAwait(false);

        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);

        _connectionLock.Dispose();
    }

    /// <summary>An observed unexpected loss of consumption, surfaced by <see cref="CheckHealthAsync"/>.</summary>
    private sealed record ConsumerFault(DateTimeOffset OccurredAtUtc, string Reason);
}
