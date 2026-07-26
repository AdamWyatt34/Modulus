using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;
using Modulus.Messaging.Outbox;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Retention;

// Sqlite in-memory rather than the EF InMemory provider: the bulk purge path depends on
// ExecuteDeleteAsync, which only relational providers translate.
public sealed class EfAdminStoreRetentionTests : IDisposable
{
    // One connection per context: EnsureCreated on a shared connection would no-op for the
    // second context (the database already contains tables) and leave its schema missing.
    private readonly SqliteConnection _outboxConnection;
    private readonly SqliteConnection _inboxConnection;
    private readonly OutboxDbContext _outboxContext;
    private readonly InboxDbContext _inboxContext;
    private readonly EfOutboxAdminStore _outboxAdmin;
    private readonly EfInboxAdminStore _inboxAdmin;

    public EfAdminStoreRetentionTests()
    {
        _outboxConnection = new SqliteConnection("DataSource=:memory:");
        _outboxConnection.Open();
        _outboxContext = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(_outboxConnection).Options);
        _outboxContext.Database.EnsureCreated();
        _outboxAdmin = new EfOutboxAdminStore(_outboxContext);

        _inboxConnection = new SqliteConnection("DataSource=:memory:");
        _inboxConnection.Open();
        _inboxContext = new InboxDbContext(
            new DbContextOptionsBuilder<InboxDbContext>().UseSqlite(_inboxConnection).Options);
        _inboxContext.Database.EnsureCreated();
        _inboxAdmin = new EfInboxAdminStore(_inboxContext);
    }

    public void Dispose()
    {
        _outboxContext.Dispose();
        _inboxContext.Dispose();
        _outboxConnection.Dispose();
        _inboxConnection.Dispose();
    }

    private static OutboxMessage OutboxRow(DateTime? processedAt, int attempts = 0) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "Sample.SomethingHappened, Sample",
        Payload = "{}",
        CreatedAt = DateTime.UtcNow.AddDays(-30),
        ProcessedAt = processedAt,
        Attempts = attempts,
    };

    private async Task<InboxMessage> SeedInboxRow(DateTime occurredOnUtc, params string[] handlers)
    {
        var message = new InboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "Sample.SomethingHappened, Sample",
            Content = "{}",
            OccurredOnUtc = occurredOnUtc,
        };
        _inboxContext.InboxMessages.Add(message);

        foreach (var handler in handlers)
        {
            _inboxContext.InboxMessageConsumers.Add(new InboxMessageConsumer
            {
                InboxMessageId = message.Id,
                Name = handler,
                ReservedOnUtc = occurredOnUtc,
                ProcessedOnUtc = occurredOnUtc,
            });
        }

        await _inboxContext.SaveChangesAsync();
        _inboxContext.ChangeTracker.Clear();
        return message;
    }

    // ── Outbox ───────────────────────────────────────────────────

    [Fact]
    public async Task PurgeProcessed_removes_only_processed_rows_older_than_cutoff()
    {
        var oldProcessed = OutboxRow(processedAt: DateTime.UtcNow.AddDays(-10));
        var recentProcessed = OutboxRow(processedAt: DateTime.UtcNow.AddHours(-1));
        var pending = OutboxRow(processedAt: null);
        var deadLettered = OutboxRow(processedAt: null, attempts: 5);
        _outboxContext.OutboxMessages.AddRange(oldProcessed, recentProcessed, pending, deadLettered);
        await _outboxContext.SaveChangesAsync();
        _outboxContext.ChangeTracker.Clear();

        var purged = await _outboxAdmin.PurgeProcessedAsync(DateTime.UtcNow.AddDays(-7), batchSize: 100);

        purged.ShouldBe(1);
        var remaining = await _outboxContext.OutboxMessages.AsNoTracking().Select(m => m.Id).ToListAsync();
        remaining.ShouldBe([recentProcessed.Id, pending.Id, deadLettered.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task PurgeProcessed_respects_batch_size_and_deletes_oldest_first()
    {
        var oldest = OutboxRow(processedAt: DateTime.UtcNow.AddDays(-30));
        var middle = OutboxRow(processedAt: DateTime.UtcNow.AddDays(-20));
        var newest = OutboxRow(processedAt: DateTime.UtcNow.AddDays(-10));
        _outboxContext.OutboxMessages.AddRange(newest, oldest, middle);
        await _outboxContext.SaveChangesAsync();
        _outboxContext.ChangeTracker.Clear();

        var purged = await _outboxAdmin.PurgeProcessedAsync(DateTime.UtcNow.AddDays(-7), batchSize: 2);

        purged.ShouldBe(2);
        var remaining = await _outboxContext.OutboxMessages.AsNoTracking().Select(m => m.Id).ToListAsync();
        remaining.ShouldBe([newest.Id]);
    }

    [Fact]
    public async Task CountProcessed_counts_only_purgeable_rows()
    {
        _outboxContext.OutboxMessages.AddRange(
            OutboxRow(processedAt: DateTime.UtcNow.AddDays(-10)),
            OutboxRow(processedAt: DateTime.UtcNow.AddDays(-8)),
            OutboxRow(processedAt: DateTime.UtcNow.AddHours(-1)),
            OutboxRow(processedAt: null));
        await _outboxContext.SaveChangesAsync();
        _outboxContext.ChangeTracker.Clear();

        var count = await _outboxAdmin.CountProcessedAsync(DateTime.UtcNow.AddDays(-7));

        count.ShouldBe(2);
    }

    [Fact]
    public async Task PurgeProcessed_on_empty_store_returns_zero()
    {
        var purged = await _outboxAdmin.PurgeProcessedAsync(DateTime.UtcNow, batchSize: 100);

        purged.ShouldBe(0);
    }

    // ── Inbox ────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeOld_removes_old_messages_and_their_consumer_rows()
    {
        var oldMessage = await SeedInboxRow(DateTime.UtcNow.AddDays(-10), "HandlerA", "HandlerB");
        var recentMessage = await SeedInboxRow(DateTime.UtcNow.AddHours(-1), "HandlerA");

        var purged = await _inboxAdmin.PurgeOldAsync(DateTime.UtcNow.AddDays(-7), batchSize: 100);

        purged.ShouldBe(1);
        var remainingMessages = await _inboxContext.InboxMessages.AsNoTracking().Select(m => m.Id).ToListAsync();
        remainingMessages.ShouldBe([recentMessage.Id]);
        var remainingConsumers = await _inboxContext.InboxMessageConsumers.AsNoTracking().ToListAsync();
        remainingConsumers.ShouldAllBe(c => c.InboxMessageId == recentMessage.Id);
        remainingConsumers.Count.ShouldBe(1);
        _ = oldMessage;
    }

    [Fact]
    public async Task PurgeOld_respects_batch_size_oldest_first()
    {
        var oldest = await SeedInboxRow(DateTime.UtcNow.AddDays(-30));
        var middle = await SeedInboxRow(DateTime.UtcNow.AddDays(-20));
        var newest = await SeedInboxRow(DateTime.UtcNow.AddDays(-10));

        var purged = await _inboxAdmin.PurgeOldAsync(DateTime.UtcNow.AddDays(-7), batchSize: 2);

        purged.ShouldBe(2);
        var remaining = await _inboxContext.InboxMessages.AsNoTracking().Select(m => m.Id).ToListAsync();
        remaining.ShouldBe([newest.Id]);
        _ = (oldest, middle);
    }

    [Fact]
    public async Task CountOld_counts_messages_before_cutoff()
    {
        await SeedInboxRow(DateTime.UtcNow.AddDays(-10));
        await SeedInboxRow(DateTime.UtcNow.AddHours(-1));

        var count = await _inboxAdmin.CountOldAsync(DateTime.UtcNow.AddDays(-7));

        count.ShouldBe(1);
    }
}
