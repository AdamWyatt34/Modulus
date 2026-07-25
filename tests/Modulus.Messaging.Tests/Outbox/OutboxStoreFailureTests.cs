using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Outbox;

public class OutboxStoreFailureTests
{
    private static OutboxDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase($"outbox-fail-{Guid.NewGuid():N}")
            .Options;
        return new OutboxDbContext(options);
    }

    [Fact]
    public async Task MarkAsFailed_FirstFailure_IncrementsAttemptsToOne_AndStoresError()
    {
        using var db = CreateDbContext();
        var id = Guid.NewGuid();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            EventType = "Test.Event",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new EfOutboxStore(db, new FakeOutboxNotifier());
        await store.MarkAsFailed(id, "transient network blip", nextAttemptOnUtc: null);

        var reloaded = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.Attempts.ShouldBe(1);
        reloaded.LastError.ShouldBe("transient network blip");
        reloaded.NextAttemptOnUtc.ShouldBeNull();
    }

    [Fact]
    public async Task MarkAsFailed_MultipleFailures_AccumulatesAttempts()
    {
        using var db = CreateDbContext();
        var id = Guid.NewGuid();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            EventType = "Test.Event",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var store = new EfOutboxStore(db, new FakeOutboxNotifier());
        await store.MarkAsFailed(id, "attempt 1", nextAttemptOnUtc: null);
        await store.MarkAsFailed(id, "attempt 2", nextAttemptOnUtc: null);
        await store.MarkAsFailed(id, "attempt 3", nextAttemptOnUtc: null);

        var reloaded = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.Attempts.ShouldBe(3);
        reloaded.LastError.ShouldBe("attempt 3");
    }

    [Fact]
    public async Task MarkAsFailed_WithNextAttemptOnUtc_PersistsBackoffTimestamp()
    {
        using var db = CreateDbContext();
        var id = Guid.NewGuid();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            EventType = "Test.Event",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var nextAttempt = DateTime.UtcNow.AddMinutes(5);
        var store = new EfOutboxStore(db, new FakeOutboxNotifier());
        await store.MarkAsFailed(id, "broker unavailable", nextAttempt);

        var reloaded = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.NextAttemptOnUtc.ShouldBe(nextAttempt);
    }

    [Fact]
    public async Task GetPending_ExcludesRowsBackedOffIntoTheFuture()
    {
        // Regression for the poison-row hot loop (C3 / H-MSG3): once a row is marked failed
        // with a future NextAttemptOnUtc, GetPending must not return it again until that time
        // elapses — otherwise the dispatcher would refetch and re-fail it every single pass.
        using var db = CreateDbContext();
        var baseTime = DateTime.UtcNow.AddMinutes(-10);

        var backedOffId = Guid.NewGuid();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = backedOffId,
            EventType = "Test.Event",
            Payload = "{}",
            CreatedAt = baseTime,
            Attempts = 1,
            NextAttemptOnUtc = DateTime.UtcNow.AddMinutes(5),
        });

        var eligibleId = Guid.NewGuid();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = eligibleId,
            EventType = "Test.Event",
            Payload = "{}",
            CreatedAt = baseTime.AddSeconds(1),
            Attempts = 1,
            NextAttemptOnUtc = DateTime.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();

        var store = new EfOutboxStore(db, new FakeOutboxNotifier());
        var pending = await store.GetPending(10, maxAttempts: 5);

        pending.Count.ShouldBe(1);
        pending[0].Id.ShouldBe(eligibleId);
    }

    [Fact]
    public async Task GetPending_ExcludesDeadLetteredRows_SoNewerMessagesAreNotStarved()
    {
        // Regression: if the oldest OutboxBatchSize rows are all dead-lettered, GetPending
        // must skip past them at the DB level so newer fresh rows still come back.
        using var db = CreateDbContext();
        const int batchSize = 3;
        const int maxAttempts = 5;
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 3 dead-lettered rows (oldest)
        for (var i = 0; i < batchSize; i++)
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "Test.DeadEvent",
                Payload = "{}",
                CreatedAt = baseTime.AddSeconds(i),
                Attempts = maxAttempts, // already at the dead-letter threshold
            });
        }
        // 1 fresh row (newer)
        var freshId = Guid.NewGuid();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = freshId,
            EventType = "Test.FreshEvent",
            Payload = "{}",
            CreatedAt = baseTime.AddMinutes(1),
        });
        await db.SaveChangesAsync();

        var store = new EfOutboxStore(db, new FakeOutboxNotifier());
        var pending = await store.GetPending(batchSize, maxAttempts);

        pending.Count.ShouldBe(1);
        pending[0].Id.ShouldBe(freshId);
    }
}
