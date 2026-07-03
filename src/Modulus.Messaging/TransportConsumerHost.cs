using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Dispatch;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging;

/// <summary>
/// Starts message consumption on the configured transport for every subscription in the
/// catalog, routing received envelopes through the <see cref="ConsumerDispatcher"/>.
/// Registered before the outbox processor so consumers are subscribed before the first
/// outbox dispatch pass (the in-memory transport drops messages published with no subscriber),
/// and stopped after it so in-flight messages drain once publishing has ceased.
/// </summary>
internal sealed class TransportConsumerHost(
    IMessageTransport transport,
    ConsumerDispatcher dispatcher,
    TransportSubscriptionCatalog catalog,
    ILogger<TransportConsumerHost> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (catalog.Subscriptions.Count == 0)
        {
            logger.LogDebug("No integration event handlers discovered; running as a publish-only host.");
            return;
        }

        await transport
            .StartConsumingAsync(catalog.Subscriptions, dispatcher.DispatchAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => catalog.Subscriptions.Count == 0
            ? Task.CompletedTask
            : transport.StopConsumingAsync(cancellationToken);
}
