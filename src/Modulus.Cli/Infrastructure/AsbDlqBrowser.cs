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

    private readonly ServiceBusClient _client = new(connection.ConnectionString);

    private string Topic => AzureServiceBusTopology.TopicName(connection.EventTypeName!);
    private string Subscription => AzureServiceBusTopology.SubscriptionName(connection.EndpointName);

    private ServiceBusReceiver CreateDlqReceiver()
        => _client.CreateReceiver(Topic, Subscription, new ServiceBusReceiverOptions
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

    public async Task<bool> ReplayAsync(string messageId, int max, CancellationToken cancellationToken = default)
        => await ReplayCoreAsync(messageId, max, cancellationToken).ConfigureAwait(false) > 0;

    public Task<int> ReplayAllAsync(int max, CancellationToken cancellationToken = default)
        => ReplayCoreAsync(messageId: null, max, cancellationToken);

    private async Task<int> ReplayCoreAsync(string? messageId, int max, CancellationToken cancellationToken)
    {
        await using var receiver = CreateDlqReceiver();
        await using var sender = _client.CreateSender(Topic);

        var replayed = 0;
        var examined = 0;

        while (examined < max)
        {
            var batch = await receiver
                .ReceiveMessagesAsync(Math.Min(32, max - examined), ReceiveWait, cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
                break;

            foreach (var received in batch)
            {
                examined++;

                var isMatch = messageId is null
                    || string.Equals(received.MessageId, messageId, StringComparison.OrdinalIgnoreCase);

                if (!isMatch)
                {
                    await receiver.AbandonMessageAsync(received, cancellationToken: cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // The copy constructor carries body, MessageId, and application properties.
                await sender.SendMessageAsync(new ServiceBusMessage(received), cancellationToken).ConfigureAwait(false);
                await receiver.CompleteMessageAsync(received, cancellationToken).ConfigureAwait(false);
                replayed++;

                if (messageId is not null)
                    return replayed;
            }
        }

        return replayed;
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
