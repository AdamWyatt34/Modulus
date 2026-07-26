using System.Collections;
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
    private const string DeathHeader = "x-death";
    private const string DeathCountField = "count";
    private const string DeliveryAttemptHeader = "modulus-delivery-attempt";

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
                ReadEnqueuedAt(result.BasicProperties),
                ReadHeader(result.BasicProperties, FirstDeathReasonHeader),
                ReadDeathCount(result.BasicProperties)));
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

                // Empty x-first-death-exchange means the first death happened via the DEFAULT
                // exchange — the shape of every broker-retried or scheduled message (their
                // copies are published to "" with the queue as routing key). Replaying to ""
                // with an empty routing key would be silently unroutable-and-confirmed, so an
                // empty value must fall back to the event-type exchange like a missing one.
                var firstDeathExchange = ReadHeader(result.BasicProperties, FirstDeathExchangeHeader);
                var exchange = string.IsNullOrWhiteSpace(firstDeathExchange)
                    ? RabbitMqTopology.ExchangeName(result.BasicProperties.Type ?? string.Empty)
                    : firstDeathExchange;

                var replayProperties = new BasicProperties(result.BasicProperties);
                // A broker-retried message dead-lettered with its attempt budget spent; a
                // replay is a fresh operator-initiated run and must get the full budget again.
                if (replayProperties.Headers is { } replayHeaders)
                    replayHeaders.Remove(DeliveryAttemptHeader);

                // Confirmations are on and the publish is mandatory: BasicPublishAsync completes
                // only when the broker confirms a *routed* message, so the dead-lettered copy is
                // acked only after the replay is provably safe (an unroutable replay faults).
                await channel.BasicPublishAsync(
                    exchange,
                    routingKey: string.Empty,
                    mandatory: true,
                    basicProperties: replayProperties,
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

    /// <summary>
    /// Populates "enqueued at" from the AMQP timestamp property when the broker (or the
    /// publisher) actually set one — it's an optional property, not always present.
    /// </summary>
    private static DateTimeOffset? ReadEnqueuedAt(IReadOnlyBasicProperties properties)
        => properties.IsTimestampPresent()
            ? DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime)
            : null;

    /// <summary>
    /// Reads the delivery count from the AMQP-native <c>x-death</c> header — the per-message
    /// count of times *this* message has been dead-lettered for its most recent reason/queue —
    /// rather than <c>BasicGetResult.MessageCount</c>, which is the dead-letter *queue's*
    /// remaining depth at the moment of the get, not a per-message counter at all. Returns null
    /// (rendered as "-" by callers) when the header is absent, which is possible if a message
    /// was dead-lettered by a mechanism that doesn't populate it.
    /// </summary>
    private static long? ReadDeathCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is not { } headers
            || !headers.TryGetValue(DeathHeader, out var raw)
            || raw is not IEnumerable deaths)
        {
            return null;
        }

        foreach (var entry in deaths)
        {
            if (entry is not IDictionary table || !table.Contains(DeathCountField))
                continue;

            return table[DeathCountField] switch
            {
                long l => l,
                int i => i,
                short s => s,
                byte b => b,
                ulong ul => unchecked((long)ul),
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
