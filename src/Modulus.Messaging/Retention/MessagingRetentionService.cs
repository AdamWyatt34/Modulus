using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;

namespace Modulus.Messaging.Retention;

/// <summary>
/// Background sweep that enforces <see cref="RetentionOptions"/>: permanently deletes outbox
/// rows published more than <see cref="RetentionOptions.ProcessedOutboxAge"/> ago and inbox
/// rows older than <see cref="RetentionOptions.InboxAge"/>, in
/// <see cref="RetentionOptions.PurgeBatchSize"/>-row batches until each store is drained.
/// Registered by <c>AddModulusMessaging</c> only when retention is enabled. A store whose
/// database context is not registered (outbox-only or inbox-only hosts) is skipped after one
/// warning rather than failing the sweep.
/// </summary>
internal sealed class MessagingRetentionService(
    IServiceScopeFactory scopeFactory,
    ILogger<MessagingRetentionService> logger,
    MessagingOptions options,
    MessagingMetrics metrics) : BackgroundService
{
    private bool _outboxUnavailable;
    private bool _inboxUnavailable;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First sweep waits a full interval: startup is the worst moment to add delete load,
        // and a fresh deployment has nothing old enough to purge anyway.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.Retention.SweepInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await SweepAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        if (!_outboxUnavailable)
        {
            _outboxUnavailable = !await TryPurgeAsync(
                "outbox",
                DateTime.UtcNow - options.Retention.ProcessedOutboxAge,
                static provider => provider.GetService<IOutboxAdminStore>(),
                static (store, olderThan, batchSize, ct) => store.PurgeProcessedAsync(olderThan, batchSize, ct),
                cancellationToken).ConfigureAwait(false);
        }

        if (!_inboxUnavailable)
        {
            _inboxUnavailable = !await TryPurgeAsync(
                "inbox",
                DateTime.UtcNow - options.Retention.InboxAge,
                static provider => provider.GetService<IInboxAdminStore>(),
                static (store, olderThan, batchSize, ct) => store.PurgeOldAsync(olderThan, batchSize, ct),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs one store's purge to completion in batches. Returns <see langword="false"/> only
    /// when the store cannot be RESOLVED (not registered, or its DbContext dependency is
    /// missing) — a topology choice that permanently disables that store's sweep. Purge-time
    /// errors, transient or not, always log and retry on the next sweep: a database hiccup on
    /// the first batch must never silently turn retention off for the process lifetime.
    /// </summary>
    private async Task<bool> TryPurgeAsync<TStore>(
        string storeName,
        DateTime olderThanUtc,
        Func<IServiceProvider, TStore?> resolve,
        Func<TStore, DateTime, int, CancellationToken, Task<int>> purge,
        CancellationToken cancellationToken)
        where TStore : class
    {
        var batchSize = options.Retention.PurgeBatchSize;
        long total = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // One scope per batch keeps the change-tracker-free contexts short-lived and
                // lets long drains span connection recycling.
                using var scope = scopeFactory.CreateScope();

                TStore? store;
                try
                {
                    store = resolve(scope.ServiceProvider);
                }
                catch (InvalidOperationException ex)
                {
                    // GetService throws when the registration exists but its dependencies
                    // (the store's DbContext) don't — same topology signal as no registration.
                    logger.LogWarning(
                        ex,
                        "Messaging retention cannot construct the {Store} admin store; {Store} retention is disabled for this host.",
                        storeName);
                    return false;
                }

                if (store is null)
                {
                    logger.LogWarning(
                        "Messaging retention found no {Store} admin store registration; {Store} retention is disabled for this host.",
                        storeName);
                    return false;
                }

                var purged = await purge(store, olderThanUtc, batchSize, cancellationToken).ConfigureAwait(false);

                total += purged;
                if (purged > 0)
                    metrics.RetentionPurged(storeName, purged);

                if (purged < batchSize)
                    break;
            }

            if (total > 0)
            {
                logger.LogInformation(
                    "Messaging retention purged {Count} {Store} row(s) older than {Cutoff:O}.",
                    total,
                    storeName,
                    olderThanUtc);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown mid-drain: whatever remains is picked up next sweep.
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Messaging retention sweep failed for the {Store} store after purging {Count} row(s); it will retry next sweep.",
                storeName,
                total);
        }

        return true;
    }
}
