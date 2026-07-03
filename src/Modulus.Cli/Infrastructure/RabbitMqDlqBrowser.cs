using System.Text;
using Modulus.Cli.Handlers;
using Modulus.Messaging.RabbitMq;
using RabbitMQ.Client;

namespace Modulus.Cli.Infrastructure;

/// <summary>
/// DLQ access for RabbitMQ over the <c>{endpoint}.dead-letter</c> queue. RabbitMQ has no true
/// peek: listing uses basic.get and then requeues everything unacknowledged, which resets
/// delivery order and bumps redelivery flags. Replay re-publishes to the exchange the message
/// first died from (falling back to its event type's exchange) with publisher confirmations,
/// and only acknowledges the dead-lettered copy after the broker confirms the publish.
/// </summary>
internal sealed class RabbitMqDlqBrowser(DlqConnection connection) : IDlqBrowser
{
    private const string FirstDeathExchangeHeader = "x-first-death-exchange";
    private const string FirstDeathReasonHeader = "x-first-death-reason";

    private IConnection? _brokerConnection;
    private IChannel? _channel;

    private string DeadLetterQueue => RabbitMqTopology.DeadLetterQueueName(connection.EndpointName);

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        var factory = new ConnectionFactory { Uri = new Uri(connection.ConnectionString) };
        _brokerConnection ??= await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        _channel = await _brokerConnection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken).ConfigureAwait(false);

        return _channel;
    }

    public async Task<IReadOnlyList<DlqMessage>> ListAsync(int max, CancellationToken cancellationToken = default)
    {
        var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);

        var messages = new List<DlqMessage>();
        var deliveryTags = new List<ulong>();

        for (var i = 0; i < max; i++)
        {
            var result = await channel.BasicGetAsync(DeadLetterQueue, autoAck: false, cancellationToken).ConfigureAwait(false);
            if (result is null)
                break;

            deliveryTags.Add(result.DeliveryTag);
            messages.Add(new DlqMessage(
                result.BasicProperties.MessageId ?? "-",
                result.BasicProperties.Type ?? "-",
                EnqueuedAt: null,
                ReadHeader(result.BasicProperties, FirstDeathReasonHeader),
                DeliveryCount: (long)result.MessageCount + 1));
        }

        // Peek-by-get: hand every message back so nothing is consumed by listing.
        foreach (var tag in deliveryTags)
            await channel.BasicNackAsync(tag, multiple: false, requeue: true, cancellationToken).ConfigureAwait(false);

        return messages;
    }

    public async Task<bool> ReplayAsync(string messageId, int max, CancellationToken cancellationToken = default)
        => await ReplayCoreAsync(messageId, max, cancellationToken).ConfigureAwait(false) > 0;

    public Task<int> ReplayAllAsync(int max, CancellationToken cancellationToken = default)
        => ReplayCoreAsync(messageId: null, max, cancellationToken);

    private async Task<int> ReplayCoreAsync(string? messageId, int max, CancellationToken cancellationToken)
    {
        var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);

        var replayed = 0;
        var toRequeue = new List<ulong>();

        try
        {
            for (var i = 0; i < max; i++)
            {
                var result = await channel.BasicGetAsync(DeadLetterQueue, autoAck: false, cancellationToken).ConfigureAwait(false);
                if (result is null)
                    break;

                var isMatch = messageId is null
                    || string.Equals(result.BasicProperties.MessageId, messageId, StringComparison.OrdinalIgnoreCase);

                if (!isMatch)
                {
                    toRequeue.Add(result.DeliveryTag);
                    continue;
                }

                var exchange = ReadHeader(result.BasicProperties, FirstDeathExchangeHeader)
                    ?? RabbitMqTopology.ExchangeName(result.BasicProperties.Type ?? string.Empty);

                // Confirmations are on: BasicPublishAsync completes only when the broker
                // confirms, so the dead-lettered copy is acked only after the replay is safe.
                await channel.BasicPublishAsync(
                    exchange,
                    routingKey: string.Empty,
                    mandatory: false,
                    basicProperties: new BasicProperties(result.BasicProperties),
                    body: result.Body,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await channel.BasicAckAsync(result.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
                replayed++;

                if (messageId is not null)
                    break;
            }
        }
        finally
        {
            // Non-matching (or unprocessed) messages go back to the DLQ.
            foreach (var tag in toRequeue)
                await channel.BasicNackAsync(tag, multiple: false, requeue: true, cancellationToken).ConfigureAwait(false);
        }

        return replayed;
    }

    private static string? ReadHeader(IReadOnlyBasicProperties properties, string name)
    {
        if (properties.Headers is { } headers && headers.TryGetValue(name, out var raw))
        {
            return raw switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string s => s,
                _ => null,
            };
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync().ConfigureAwait(false);

        if (_brokerConnection is not null)
            await _brokerConnection.DisposeAsync().ConfigureAwait(false);
    }
}
