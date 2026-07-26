using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Retention;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Retention;

public class MessagingRetentionServiceTests
{
    private sealed class RecordingOutboxAdminStore : IOutboxAdminStore
    {
        public List<(DateTime OlderThan, int BatchSize)> PurgeCalls { get; } = [];

        /// <summary>Return values per call; after the queue drains, returns 0.</summary>
        public Queue<int> PurgeResults { get; } = new();

        public Task<IReadOnlyList<OutboxMessage>> GetFailedAsync(int maxAttempts, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);

        public Task<bool> RetryAsync(Guid messageId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> PurgeAsync(Guid messageId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> CountProcessedAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> PurgeProcessedAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken = default)
        {
            PurgeCalls.Add((olderThanUtc, batchSize));
            return Task.FromResult(PurgeResults.Count > 0 ? PurgeResults.Dequeue() : 0);
        }
    }

    private sealed class RecordingInboxAdminStore : IInboxAdminStore
    {
        public List<(DateTime OlderThan, int BatchSize)> PurgeCalls { get; } = [];

        public Task<int> CountOldAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> PurgeOldAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken = default)
        {
            PurgeCalls.Add((olderThanUtc, batchSize));
            return Task.FromResult(0);
        }
    }

    private static MessagingRetentionService CreateService(
        MessagingOptions options,
        RecordingOutboxAdminStore? outbox,
        RecordingInboxAdminStore? inbox)
    {
        var services = new ServiceCollection();
        if (outbox is not null)
            services.AddSingleton<IOutboxAdminStore>(outbox);
        if (inbox is not null)
            services.AddSingleton<IInboxAdminStore>(inbox);
        var provider = services.BuildServiceProvider();

        return new MessagingRetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MessagingRetentionService>.Instance,
            options,
            new MessagingMetrics(meterFactory: null));
    }

    private static MessagingOptions OptionsWithRetention(int purgeBatchSize = 500) => new()
    {
        Retention =
        {
            Enabled = true,
            ProcessedOutboxAge = TimeSpan.FromDays(3),
            InboxAge = TimeSpan.FromDays(5),
            PurgeBatchSize = purgeBatchSize,
        },
    };

    [Fact]
    public async Task Sweep_purges_both_stores_with_configured_cutoffs()
    {
        var outbox = new RecordingOutboxAdminStore();
        var inbox = new RecordingInboxAdminStore();
        var service = CreateService(OptionsWithRetention(), outbox, inbox);

        await service.SweepAsync(CancellationToken.None);

        var outboxCall = outbox.PurgeCalls.ShouldHaveSingleItem();
        outboxCall.OlderThan.ShouldBe(DateTime.UtcNow.AddDays(-3), tolerance: TimeSpan.FromMinutes(1));
        outboxCall.BatchSize.ShouldBe(500);

        var inboxCall = inbox.PurgeCalls.ShouldHaveSingleItem();
        inboxCall.OlderThan.ShouldBe(DateTime.UtcNow.AddDays(-5), tolerance: TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Sweep_repeats_batches_until_store_is_drained()
    {
        var outbox = new RecordingOutboxAdminStore();
        // Two full batches then a partial one: three calls total.
        outbox.PurgeResults.Enqueue(10);
        outbox.PurgeResults.Enqueue(10);
        outbox.PurgeResults.Enqueue(3);
        var inbox = new RecordingInboxAdminStore();
        var service = CreateService(OptionsWithRetention(purgeBatchSize: 10), outbox, inbox);

        await service.SweepAsync(CancellationToken.None);

        outbox.PurgeCalls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Sweep_with_unregistered_inbox_disables_inbox_and_still_purges_outbox()
    {
        var outbox = new RecordingOutboxAdminStore();
        var service = CreateService(OptionsWithRetention(), outbox, inbox: null);

        await service.SweepAsync(CancellationToken.None);
        await service.SweepAsync(CancellationToken.None);

        // Outbox swept both times; the unresolvable inbox store didn't fail the sweep.
        outbox.PurgeCalls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Sweep_with_neither_store_registered_is_a_noop()
    {
        var service = CreateService(OptionsWithRetention(), outbox: null, inbox: null);

        // Must not throw — both stores get disabled after the first attempt.
        await service.SweepAsync(CancellationToken.None);
        await service.SweepAsync(CancellationToken.None);
    }
}
