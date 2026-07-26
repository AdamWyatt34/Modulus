using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Outbox;

public sealed class EfOutboxStoreSchedulingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OutboxDbContext _dbContext;
    private readonly EfOutboxStore _store;

    public EfOutboxStoreSchedulingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbContext = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
        _store = new EfOutboxStore(_dbContext, new FakeOutboxNotifier());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private static TestOrderCreatedEvent NewEvent(int orderId = 1) => new()
    {
        OrderId = orderId,
        CustomerName = $"Customer {orderId}",
    };

    [Fact]
    public async Task Scheduled_save_stamps_ScheduledOnUtc()
    {
        var enqueueAt = DateTimeOffset.UtcNow.AddHours(2);

        await _store.Save(NewEvent(), enqueueAt);

        var row = await _dbContext.OutboxMessages.AsNoTracking().SingleAsync();
        row.ScheduledOnUtc.ShouldNotBeNull();
        row.ScheduledOnUtc.Value.ShouldBe(enqueueAt.UtcDateTime, tolerance: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Unscheduled_save_leaves_ScheduledOnUtc_null()
    {
        await _store.Save(NewEvent());

        var row = await _dbContext.OutboxMessages.AsNoTracking().SingleAsync();
        row.ScheduledOnUtc.ShouldBeNull();
    }

    [Fact]
    public async Task GetPending_excludes_future_scheduled_rows_and_includes_due_ones()
    {
        var immediate = NewEvent(1);
        var due = NewEvent(2);
        var future = NewEvent(3);
        await _store.Save(immediate);
        await _store.Save(due, DateTimeOffset.UtcNow.AddMinutes(-5));
        await _store.Save(future, DateTimeOffset.UtcNow.AddHours(1));

        var pending = await _store.GetPending(batchSize: 10, maxAttempts: 5);

        pending.Select(m => m.Id).ShouldBe([immediate.EventId, due.EventId], ignoreOrder: true);
    }

    [Fact]
    public async Task CountPending_excludes_future_scheduled_rows()
    {
        // A message scheduled a week out is not outstanding work — it must not count toward
        // the backlog the health check alarms on.
        await _store.Save(NewEvent(1));
        await _store.Save(NewEvent(2), DateTimeOffset.UtcNow.AddDays(7));

        var count = await _store.CountPending(maxAttempts: 5);

        count.ShouldBe(1);
    }
}
