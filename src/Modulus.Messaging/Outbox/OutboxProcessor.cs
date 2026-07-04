using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Diagnostics;

namespace Modulus.Messaging.Outbox;

internal sealed class OutboxProcessor(
    IOutboxDispatcher dispatcher,
    IOutboxNotifier notifier,
    ILogger<OutboxProcessor> logger,
    MessagingOptions options,
    MessagingMetrics metrics) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var fetched = 0;
            try
            {
                fetched = await dispatcher.DispatchPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing outbox messages");
            }

            // A full batch means more rows are probably waiting — drain before sleeping.
            if (fetched >= options.OutboxBatchSize)
            {
                metrics.OutboxWakeup("backlog");
                continue;
            }

            // A dispatch failure falls through to the wait, preserving the poll interval
            // as the error backoff (no hot spin against a persistently failing store).
            try
            {
                var signaled = await notifier
                    .WaitAsync(options.OutboxPollInterval, stoppingToken)
                    .ConfigureAwait(false);
                metrics.OutboxWakeup(signaled ? "signal" : "poll");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
