using Azure.Messaging.ServiceBus;
using Modulus.Cli.Handlers;
using Modulus.Messaging.AzureServiceBus;

namespace Modulus.Cli.Infrastructure;

/// <summary>
/// DLQ access for Azure Service Bus over the subscription's built-in dead-letter sub-queue.
/// Listing uses a true peek. Replay clones the message (body, MessageId, application
/// properties) and sends it back to the originating topic — Service Bus has no native
/// resubmit, so broker-set system properties (enqueue time, sequence number) are new on the
/// replayed copy.
/// </summary>
internal sealed class AsbDlqBrowser(DlqConnection connection) : IDlqBrowser
{
    private static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(3);

    private ServiceBusClient? _client;

    /// <summary>
    /// Built lazily, on first actual use, rather than in the primary constructor: constructing a
    /// <see cref="ServiceBusClient"/> parses (and can reject) the connection string, and
    /// <c>DlqHandler</c> wraps both browser construction and every browser call in the same
    /// try/catch. Laziness keeps that failure surfaced from wherever it's actually triggered —
    /// and keeps this type trivially, side-effect-free constructible.
    /// </summary>
    private ServiceBusClient Client => _client ??= new ServiceBusClient(connection.ConnectionString);

    private string Topic => AzureServiceBusTopology.TopicName(connection.EventTypeName!);
    private string Subscription => AzureServiceBusTopology.SubscriptionName(connection.EndpointName);

    private ServiceBusReceiver CreateDlqReceiver()
        => Client.CreateReceiver(Topic, Subscription, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });

    public async Task<IReadOnlyList<DlqMessage>> ListAsync(int max, CancellationToken cancellationToken = default)
    {
        await using var receiver = CreateDlqReceiver();
        var peeked = await receiver.PeekMessagesAsync(max, cancellationToken: cancellationToken).ConfigureAwait(false);

        return peeked
            .Select(m => new DlqMessage(
                m.MessageId,
                m.Subject ?? "-",
                m.EnqueuedTime,
                m.DeadLetterReason,
                m.DeliveryCount))
            .ToList();
    }

    public Task<bool> ReplayAsync(string messageId, int max, CancellationToken cancellationToken = default)
        => ReplaySingleAsync(messageId, max, cancellationToken);

    public Task<int> ReplayAllAsync(int max, CancellationToken cancellationToken = default)
        => ReplayAllCoreAsync(max, cancellationToken);

    /// <summary>
    /// Finds one message by id among up to <paramref name="max"/> dead-lettered messages.
    /// Collects the whole scan window first and settles every examined message only after the
    /// search completes — the same collect-then-settle shape as <c>RabbitMqDlqBrowser</c>.
    /// Abandoning a non-matching message *mid-scan* (the previous shape) returns it to the head
    /// of the sub-queue immediately, where the very next receive call can pick it right back up —
    /// starving the scan from ever reaching messages deeper in the queue while inflating that
    /// head message's DeliveryCount on every pass. Settling afterward guarantees forward progress
    /// and touches each examined message's delivery count at most once. On a match, the matched
    /// message is still sent to its destination and completed before anything is abandoned.
    /// </summary>
    private async Task<bool> ReplaySingleAsync(string messageId, int max, CancellationToken cancellationToken)
    {
        await using var receiver = CreateDlqReceiver();
        await using var sender = Client.CreateSender(Topic);

        var received = new List<ServiceBusReceivedMessage>();
        ServiceBusReceivedMessage? match = null;

        while (received.Count < max)
        {
            var batch = await receiver
                .ReceiveMessagesAsync(Math.Min(32, max - received.Count), ReceiveWait, cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
                break;

            received.AddRange(batch);
            match ??= batch.FirstOrDefault(m => string.Equals(m.MessageId, messageId, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                break;
        }

        try
        {
            if (match is null)
                return false;

            // The copy constructor carries body, MessageId, and application properties.
            await sender.SendMessageAsync(BuildReplayMessage(match), cancellationToken).ConfigureAwait(false);
            await receiver.CompleteMessageAsync(match, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            foreach (var candidate in received)
            {
                if (ReferenceEquals(candidate, match))
                    continue;

                await receiver.AbandonMessageAsync(candidate, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<int> ReplayAllCoreAsync(int max, CancellationToken cancellationToken)
    {
        await using var receiver = CreateDlqReceiver();
        await using var sender = Client.CreateSender(Topic);

        var replayed = 0;

        while (replayed < max)
        {
            var batch = await receiver
                .ReceiveMessagesAsync(Math.Min(32, max - replayed), ReceiveWait, cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
                break;

            foreach (var message in batch)
            {
                await sender.SendMessageAsync(BuildReplayMessage(message), cancellationToken).ConfigureAwait(false);
                await receiver.CompleteMessageAsync(message, cancellationToken).ConfigureAwait(false);
                replayed++;
            }
        }

        return replayed;
    }

    public ValueTask DisposeAsync() => _client?.DisposeAsync() ?? ValueTask.CompletedTask;
    /// <summary>
    /// Builds the replay copy: same body and properties, minus the delivery-attempt header —
    /// a broker-retried message dead-lettered with its budget spent, and an operator replay
    /// must get the full budget again instead of one pass straight back to the DLQ.
    /// </summary>
    private static ServiceBusMessage BuildReplayMessage(ServiceBusReceivedMessage source)
    {
        var copy = new ServiceBusMessage(source);
        copy.ApplicationProperties.Remove("modulus-delivery-attempt");
        return copy;
    }

}
