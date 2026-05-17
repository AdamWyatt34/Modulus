using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
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

        var store = new EfOutboxStore(db);
        await store.MarkAsFailed(id, "transient network blip");

        var reloaded = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.Attempts.ShouldBe(1);
        reloaded.LastError.ShouldBe("transient network blip");
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

        var store = new EfOutboxStore(db);
        await store.MarkAsFailed(id, "attempt 1");
        await store.MarkAsFailed(id, "attempt 2");
        await store.MarkAsFailed(id, "attempt 3");

        var reloaded = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.Attempts.ShouldBe(3);
        reloaded.LastError.ShouldBe("attempt 3");
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

        var store = new EfOutboxStore(db);
        var pending = await store.GetPending(batchSize, maxAttempts);

        pending.Count.ShouldBe(1);
        pending[0].Id.ShouldBe(freshId);
    }
}
