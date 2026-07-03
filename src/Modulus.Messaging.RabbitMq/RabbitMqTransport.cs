using Microsoft.Extensions.Logging;
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
/// </summary>
internal sealed class RabbitMqTransport(
    MessagingOptions options,
    ILogger<RabbitMqTransport> logger) : IMessageTransport
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly HashSet<string> _declaredExchanges = new(StringComparer.Ordinal);
    private readonly HashSet<string> _declaredSendQueues = new(StringComparer.Ordinal);

    private IConnection? _connection;
    private IChannel? _publishChannel;
    private IChannel? _consumeChannel;
    private string? _consumerTag;

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

            _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            return _connection;
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

        if (options.AutoProvision && !_declaredExchanges.Contains(exchange))
        {
            await channel.ExchangeDeclareAsync(
                exchange, ExchangeType.Fanout, durable: true, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _declaredExchanges.Add(exchange);
        }

        await channel.BasicPublishAsync(
            exchange,
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: RabbitMqEnvelopeMapper.ToBasicProperties(envelope),
            body: envelope.Body,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(TransportEnvelope envelope, string queueName, CancellationToken cancellationToken = default)
    {
        var channel = await GetPublishChannelAsync(cancellationToken).ConfigureAwait(false);
        var queue = RabbitMqTopology.SendQueueName(queueName);

        if (options.AutoProvision && !_declaredSendQueues.Contains(queue))
        {
            await channel.QueueDeclareAsync(
                queue, durable: true, exclusive: false, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _declaredSendQueues.Add(queue);
        }

        // Default exchange routes by queue name.
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queue,
            mandatory: false,
            basicProperties: RabbitMqEnvelopeMapper.ToBasicProperties(envelope),
            body: envelope.Body,
            cancellationToken: cancellationToken).ConfigureAwait(false);
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

        _consumeChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: (ushort)Math.Clamp(options.PrefetchCount, 1, ushort.MaxValue)),
            cancellationToken).ConfigureAwait(false);

        if (options.AutoProvision)
        {
            await _consumeChannel.ExchangeDeclareAsync(
                deadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _consumeChannel.QueueDeclareAsync(
                RabbitMqTopology.DeadLetterQueueName(endpointName),
                durable: true, exclusive: false, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _consumeChannel.QueueBindAsync(
                RabbitMqTopology.DeadLetterQueueName(endpointName), deadLetterExchange, routingKey: string.Empty,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _consumeChannel.QueueDeclareAsync(
                queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = deadLetterExchange,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var subscription in subscriptions)
            {
                var exchange = RabbitMqTopology.ExchangeName(subscription.MessageTypeName);

                await _consumeChannel.ExchangeDeclareAsync(
                    exchange, ExchangeType.Fanout, durable: true, autoDelete: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await _consumeChannel.QueueBindAsync(
                    queue, exchange, routingKey: string.Empty,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        await _consumeChannel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)Math.Clamp(options.PrefetchCount, 1, ushort.MaxValue),
            global: false,
            cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var channel = _consumeChannel;
            if (channel is null)
                return;

            try
            {
                var envelope = RabbitMqEnvelopeMapper.ToEnvelope(delivery.BasicProperties, delivery.Body);
                var result = await onMessage(envelope, delivery.CancellationToken).ConfigureAwait(false);

                if (result == MessageDispatchResult.Acknowledge)
                {
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, delivery.CancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    // requeue: false routes through the queue's dead-letter exchange.
                    await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, delivery.CancellationToken)
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
        };

        _consumerTag = await _consumeChannel.BasicConsumeAsync(
            queue, autoAck: false, consumer, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopConsumingAsync(CancellationToken cancellationToken = default)
    {
        if (_consumeChannel is { IsOpen: true } channel && _consumerTag is not null)
        {
            await channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken).ConfigureAwait(false);
            _consumerTag = null;
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
}
