using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Outbox;

// Sqlite in-memory rather than the EF InMemory provider: the interceptor's whole contract
// is about real transaction boundaries (commit vs rollback), which InMemory doesn't have.
public sealed class OutboxNotifyingInterceptorTests : IDisposable
{
    /// <summary>Mirrors the scaffolded BaseDbContext shape: the application's own context
    /// mapping the library's OutboxMessage type alongside unrelated entities.</summary>
    private sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OutboxMessage>().HasKey(e => e.Id);
            modelBuilder.Entity<Widget>().HasKey(e => e.Id);
        }
    }

    private sealed class Widget
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "widget";
    }

    private readonly SqliteConnection _connection;
    private readonly FakeOutboxNotifier _notifier = new();
    private readonly OutboxNotifyingInterceptor _interceptor;

    public OutboxNotifyingInterceptorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _interceptor = new OutboxNotifyingInterceptor(_notifier);

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_interceptor)
            .Options;
        return new AppDbContext(options);
    }

    private static OutboxMessage NewOutboxRow() => new()
    {
        Id = Guid.NewGuid(),
        EventType = "Test.Event",
        Payload = "{}",
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task SaveChangesAsync_OutboxRowNoTransaction_NotifiesOnce()
    {
        await using var context = CreateContext();
        context.OutboxMessages.Add(NewOutboxRow());

        await context.SaveChangesAsync();

        _notifier.NotifyCount.ShouldBe(1);
    }

    [Fact]
    public async Task SaveChangesAsync_UnrelatedEntityOnly_DoesNotNotify()
    {
        await using var context = CreateContext();
        context.Widgets.Add(new Widget());

        await context.SaveChangesAsync();

        _notifier.NotifyCount.ShouldBe(0);
    }

    [Fact]
    public async Task SaveChangesAsync_InsideTransaction_NotifiesOnlyOnCommit()
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.OutboxMessages.Add(NewOutboxRow());
        await context.SaveChangesAsync();
        _notifier.NotifyCount.ShouldBe(0);

        await transaction.CommitAsync();
        _notifier.NotifyCount.ShouldBe(1);
    }

    [Fact]
    public async Task SaveChangesAsync_TransactionRolledBack_DoesNotNotify()
    {
        await using var context = CreateContext();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.OutboxMessages.Add(NewOutboxRow());
            await context.SaveChangesAsync();

            await transaction.RollbackAsync();
        }

        _notifier.NotifyCount.ShouldBe(0);
    }

    [Fact]
    public async Task SaveChangesAsync_TwoSavesOneTransaction_NotifiesOnceOnCommit()
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.OutboxMessages.Add(NewOutboxRow());
        await context.SaveChangesAsync();
        context.OutboxMessages.Add(NewOutboxRow());
        await context.SaveChangesAsync();

        await transaction.CommitAsync();

        _notifier.NotifyCount.ShouldBe(1);
    }

    [Fact]
    public async Task SharedInterceptor_TwoContexts_TracksStatePerContext()
    {
        await using var transactional = CreateContext();
        await using var plain = CreateContext();

        await using var transaction = await transactional.Database.BeginTransactionAsync();
        transactional.OutboxMessages.Add(NewOutboxRow());
        await transactional.SaveChangesAsync();

        // The other context saving outside any transaction must notify immediately,
        // unaffected by the first context's open transaction.
        plain.OutboxMessages.Add(NewOutboxRow());
        await plain.SaveChangesAsync();
        _notifier.NotifyCount.ShouldBe(1);

        await transaction.RollbackAsync();
        _notifier.NotifyCount.ShouldBe(1);
    }

    [Fact]
    public void SaveChanges_Sync_OutboxRowNoTransaction_Notifies()
    {
        using var context = CreateContext();
        context.OutboxMessages.Add(NewOutboxRow());

        context.SaveChanges();

        _notifier.NotifyCount.ShouldBe(1);
    }

    [Fact]
    public void SaveChanges_Sync_TransactionCommit_NotifiesOnCommit()
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();

        context.OutboxMessages.Add(NewOutboxRow());
        context.SaveChanges();
        _notifier.NotifyCount.ShouldBe(0);

        transaction.Commit();
        _notifier.NotifyCount.ShouldBe(1);
    }
}
