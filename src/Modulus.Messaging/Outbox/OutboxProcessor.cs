using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modulus.Messaging.Outbox;

internal sealed class OutboxProcessor(
    IOutboxDispatcher dispatcher,
    ILogger<OutboxProcessor> logger,
    MessagingOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.DispatchPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing outbox messages");
            }

            await Task.Delay(options.OutboxPollInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
