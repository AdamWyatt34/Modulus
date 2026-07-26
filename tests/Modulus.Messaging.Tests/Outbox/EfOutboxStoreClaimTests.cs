using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Outbox;

// Sqlite in-memory rather than the EF InMemory provider: ClaimPending relies on
// ExecuteUpdateAsync, which the EF Core InMemory provider does not support.
public sealed class EfOutboxStoreClaimTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OutboxDbContext _db;
    private readonly EfOutboxStore _store;

    public EfOutboxStoreClaimTests()
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

    private OutboxDbContext CreateSecondContext()
        => new(new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(_connection).Options);

    private async Task<Guid> SeedAsync()
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
        return id;
    }

    [Fact]
    public async Task ClaimPending_EmptyStore_ReturnsEmpty()
    {
        var claimed = await _store.ClaimPending("owner-a", TimeSpan.FromMinutes(5), 10, int.MaxValue);

        claimed.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClaimPending_AlreadyClaimedWithFreshLease_ExcludesTheRow()
    {
        var id = await SeedAsync();

        var first = await _store.ClaimPending("owner-a", TimeSpan.FromMinutes(5), 10, int.MaxValue);
        first.ShouldContain(m => m.Id == id);

        // A second owner must not see a row whose lease is still comfortably in the future.
        var second = await _store.ClaimPending("owner-b", TimeSpan.FromMinutes(5), 10, int.MaxValue);
        second.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClaimPending_ExpiredLease_IsClaimableByAnotherOwner()
    {
        var id = await SeedAsync();

        // A lease so short it is already expired by the time the second claim runs.
        await _store.ClaimPending("owner-a", TimeSpan.FromMilliseconds(1), 10, int.MaxValue);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var reclaimed = await _store.ClaimPending("owner-b", TimeSpan.FromMinutes(5), 10, int.MaxValue);

        reclaimed.ShouldContain(m => m.Id == id);
        var reloaded = await _db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.ClaimedBy.ShouldBe("owner-b");
    }

    [Fact]
    public async Task ClaimPending_ExpiredLease_IsClaimableByTheSameOwnerAgain()
    {
        // A crashed instance that comes back up under the same machine name (or simply the same
        // instance re-polling after its own lease lapsed) must be able to reclaim its own
        // abandoned rows — the lease expiring is what matters, not which owner id shows up next.
        var id = await SeedAsync();

        await _store.ClaimPending("owner-a", TimeSpan.FromMilliseconds(1), 10, int.MaxValue);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var reclaimed = await _store.ClaimPending("owner-a", TimeSpan.FromMinutes(5), 10, int.MaxValue);

        reclaimed.ShouldContain(m => m.Id == id);
    }

    [Fact]
    public async Task ClaimPending_ConcurrentClaimOverTheSameCandidates_SingleWinnerPerRow()
    {
        // Real concurrency (Task.WhenAll), not sequential interleaving: a sequential pair of
        // ClaimPending calls against one shared DbContext/connection would just partition the
        // batch trivially (the first call's fetch+update+fetch fully completes before the
        // second's fetch even runs), which proves nothing about the WHERE-predicate's
        // single-winner behavior under an actual race. To drive two truly concurrent claims
        // safely, each side gets its own physical SqliteConnection against a *named, shared-cache*
        // in-memory database (Mode=Memory;Cache=Shared) — unlike sharing one SqliteConnection
        // object across threads (unsafe / not what Microsoft.Data.Sqlite supports), two separate
        // connection objects pointed at the same shared-cache database is exactly how SQLite
        // supports genuine concurrent access, with SQLite's own engine-level locking serializing
        // the two ExecuteUpdateAsync calls' writes under the hood.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"claim-race-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        // Keeps the named in-memory database alive for the test's duration — it would otherwise
        // be dropped the moment the last connection to it closes.
        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        using (var schemaContext = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connectionString).Options))
        {
            schemaContext.Database.EnsureCreated();
        }

        const int candidateCount = 6;
        var seededIds = new List<Guid>();
        using (var seedContext = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connectionString).Options))
        {
            for (var i = 0; i < candidateCount; i++)
            {
                var id = Guid.NewGuid();
                seedContext.OutboxMessages.Add(new OutboxMessage
                {
                    Id = id,
                    EventType = "Test.Event",
                    Payload = "{}",
                    CreatedAt = DateTime.UtcNow.AddSeconds(i),
                });
                seededIds.Add(id);
            }
            await seedContext.SaveChangesAsync();
        }

        using var contextA = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connectionString).Options);
        using var contextB = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(connectionString).Options);
        var storeA = new EfOutboxStore(contextA, new FakeOutboxNotifier());
        var storeB = new EfOutboxStore(contextB, new FakeOutboxNotifier());

        // Batch size covers every seeded row for both sides, so — absent the claim predicate —
        // both would attempt to grab the entire identical candidate set at once.
        var claimTaskA = storeA.ClaimPending("owner-a", TimeSpan.FromMinutes(5), candidateCount, int.MaxValue);
        var claimTaskB = storeB.ClaimPending("owner-b", TimeSpan.FromMinutes(5), candidateCount, int.MaxValue);
        var results = await Task.WhenAll(claimTaskA, claimTaskB);

        var claimedByA = results[0].Select(m => m.Id).ToHashSet();
        var claimedByB = results[1].Select(m => m.Id).ToHashSet();

        // Disjoint: no row was ever handed to both claimants.
        claimedByA.Intersect(claimedByB).ShouldBeEmpty();

        // Full coverage: between the two of them, every seeded candidate was claimed by exactly
        // one owner — no row fell through the race unclaimed.
        claimedByA.Union(claimedByB).ShouldBe(seededIds, ignoreOrder: true);
    }

    [Fact]
    public async Task MarkAsProcessed_RowLostToTakeover_IsIgnored()
    {
        var id = await SeedAsync();

        await _store.ClaimPending("owner-a", TimeSpan.FromMilliseconds(1), 10, int.MaxValue);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        // owner-b takes the row over once owner-a's lease has lapsed.
        using var secondContext = CreateSecondContext();
        var storeB = new EfOutboxStore(secondContext, new FakeOutboxNotifier());
        var claimedByB = await storeB.ClaimPending("owner-b", TimeSpan.FromMinutes(5), 10, int.MaxValue);
        claimedByB.ShouldContain(m => m.Id == id);

        // owner-a's stale in-flight pass (it never learned it lost the race) tries to flush.
        await _store.MarkAsProcessed("owner-a", [id]);

        // Not stamped processed on the loser's say-so — it is still owner-b's live, unprocessed
        // claim.
        var reloaded = await _db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.ProcessedAt.ShouldBeNull();
        reloaded.ClaimedBy.ShouldBe("owner-b");
    }

    [Fact]
    public async Task MarkAsProcessed_OwnedRow_SetsProcessedAt()
    {
        var id = await SeedAsync();
        await _store.ClaimPending("owner-a", TimeSpan.FromMinutes(5), 10, int.MaxValue);

        await _store.MarkAsProcessed("owner-a", [id]);

        var reloaded = await _db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id);
        reloaded.ProcessedAt.ShouldNotBeNull();
    }
}
