using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Outbox;

// Sqlite in-memory rather than the EF InMemory provider: ClaimPending/MarkAsFailed both rely on
// ExecuteUpdateAsync, which the EF Core InMemory provider does not support.
public sealed class OutboxStoreFailureTests : IDisposable
{
    private const string Owner = "owner-a";

    private readonly SqliteConnection _connection;
    private readonly OutboxDbContext _db;
    private readonly EfOutboxStore _store;

    public OutboxStoreFailureTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _store = new EfOutboxStore(_db, new FakeOutboxNotifier());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Guid> SeedAndClaimAsync(string owner = Owner)
    {
        var id = Guid.NewGuid();
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            EventType = "Test.Event",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var claimed = await _store.ClaimPending(owner, TimeSpan.FromMinutes(5), 10, int.MaxValue);
        claimed.ShouldContain(m => m.Id == id);
        return id;
    }

    [Fact]
    public async Task MarkAsFailed_FirstFailure_IncrementsAttemptsToOne_AndStoresError()
    {
        var id = await SeedAndClaimAsync();

        await _store.MarkAsFailed(Owner, id, "transient network blip", nextAttemptOnUtc: null);

        var reloaded = await _db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.Attempts.ShouldBe(1);
        reloaded.LastError.ShouldBe("transient network blip");
        reloaded.NextAttemptOnUtc.ShouldBeNull();
    }

    [Fact]
    public async Task MarkAsFailed_ClearsTheClaim()
    {
        // A durably-recorded failure must be immediately reclaimable once its backoff elapses —
        // not stuck waiting out the rest of this pass's lease.
        var id = await SeedAndClaimAsync();

        await _store.MarkAsFailed(Owner, id, "transient", nextAttemptOnUtc: null);

        var reloaded = await _db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.ClaimedBy.ShouldBeNull();
        reloaded.ClaimedUntil.ShouldBeNull();

        // Immediately reclaimable by a different owner without waiting for anything to expire.
        var reclaimed = await _store.ClaimPending("owner-b", TimeSpan.FromMinutes(5), 10, int.MaxValue);
        reclaimed.ShouldContain(m => m.Id == id);
    }

    [Fact]
    public async Task MarkAsFailed_WrongOwner_IsANoOp()
    {
        // The claim was taken over by someone else (this owner's lease already expired) by the
        // time this call runs — stamping Attempts/LastError on the loser's say-so would be wrong.
        var id = await SeedAndClaimAsync(owner: "owner-a");

        await _store.MarkAsFailed("owner-b", id, "should not apply", nextAttemptOnUtc: null);

        var reloaded = await _db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.Attempts.ShouldBe(0);
        reloaded.LastError.ShouldBeNull();
        reloaded.ClaimedBy.ShouldBe("owner-a");
    }

    [Fact]
    public async Task MarkAsFailed_MultipleFailures_AccumulatesAttempts()
    {
        var id = await SeedAndClaimAsync();

        await _store.MarkAsFailed(Owner, id, "attempt 1", nextAttemptOnUtc: null);
        // Each MarkAsFailed clears the claim, so re-claim between calls the way the real
        // dispatcher would across passes.
        await _store.ClaimPending(Owner, TimeSpan.FromMinutes(5), 10, int.MaxValue);
        await _store.MarkAsFailed(Owner, id, "attempt 2", nextAttemptOnUtc: null);
        await _store.ClaimPending(Owner, TimeSpan.FromMinutes(5), 10, int.MaxValue);
        await _store.MarkAsFailed(Owner, id, "attempt 3", nextAttemptOnUtc: null);

        var reloaded = await _db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.Attempts.ShouldBe(3);
        reloaded.LastError.ShouldBe("attempt 3");
    }

    [Fact]
    public async Task MarkAsFailed_WithNextAttemptOnUtc_PersistsBackoffTimestamp()
    {
        var id = await SeedAndClaimAsync();

        var nextAttempt = DateTime.UtcNow.AddMinutes(5);
        await _store.MarkAsFailed(Owner, id, "broker unavailable", nextAttempt);

        var reloaded = await _db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.NextAttemptOnUtc.ShouldBe(nextAttempt);
    }

    [Fact]
    public async Task ClaimPending_ExcludesRowsBackedOffIntoTheFuture()
    {
        // Regression for the poison-row hot loop (C3 / H-MSG3): once a row is marked failed
        // with a future NextAttemptOnUtc, ClaimPending must not return it again until that time
        // elapses — otherwise the dispatcher would refetch and re-fail it every single pass.
        var baseTime = DateTime.UtcNow.AddMinutes(-10);

        var backedOffId = Guid.NewGuid();
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = backedOffId,
            EventType = "Test.Event",
            Payload = "{}",
            CreatedAt = baseTime,
            Attempts = 1,
            NextAttemptOnUtc = DateTime.UtcNow.AddMinutes(5),
        });

        var eligibleId = Guid.NewGuid();
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = eligibleId,
            EventType = "Test.Event",
            Payload = "{}",
            CreatedAt = baseTime.AddSeconds(1),
            Attempts = 1,
            NextAttemptOnUtc = DateTime.UtcNow.AddMinutes(-1),
        });
        await _db.SaveChangesAsync();

        var pending = await _store.ClaimPending(Owner, TimeSpan.FromMinutes(5), 10, maxAttempts: 5);

        pending.Count.ShouldBe(1);
        pending[0].Id.ShouldBe(eligibleId);
    }

    [Fact]
    public async Task ClaimPending_ExcludesDeadLetteredRows_SoNewerMessagesAreNotStarved()
    {
        // Regression: if the oldest batch-size rows are all dead-lettered, ClaimPending must
        // skip past them at the DB level so newer fresh rows still come back.
        const int batchSize = 3;
        const int maxAttempts = 5;
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 3 dead-lettered rows (oldest)
        for (var i = 0; i < batchSize; i++)
        {
            _db.OutboxMessages.Add(new OutboxMessage
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
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = freshId,
            EventType = "Test.FreshEvent",
            Payload = "{}",
            CreatedAt = baseTime.AddMinutes(1),
        });
        await _db.SaveChangesAsync();

        var pending = await _store.ClaimPending(Owner, TimeSpan.FromMinutes(5), batchSize, maxAttempts);

        pending.Count.ShouldBe(1);
        pending[0].Id.ShouldBe(freshId);
    }
}
