using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Inbox;

// Sqlite in-memory rather than the EF InMemory provider: the reservation contract depends
// on the composite primary key actually being enforced and on ExecuteUpdateAsync.
public sealed class EfInboxStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly InboxDbContext _dbContext;
    private readonly EfInboxStore _store;

    public EfInboxStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<InboxDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new InboxDbContext(options);
        _dbContext.Database.EnsureCreated();
        _store = new EfInboxStore(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private InboxDbContext CreateSecondContext()
    {
        var options = new DbContextOptionsBuilder<InboxDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new InboxDbContext(options);
    }

    [Fact]
    public async Task Save_ValidMessage_PersistsToDatabase()
    {
        // Arrange
        var @event = new TestIntegrationEvent();

        // Act
        await _store.Save(@event);

        // Assert
        var messages = await _dbContext.InboxMessages.AsNoTracking().ToListAsync();
        messages.Count.ShouldBe(1);
        messages[0].Id.ShouldBe(@event.EventId);
        messages[0].Type.ShouldContain(nameof(TestIntegrationEvent));
        messages[0].Content.ShouldNotBeNullOrEmpty();
        messages[0].ProcessedOnUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Save_DuplicateMessage_DoesNotThrow()
    {
        // Arrange
        var @event = new TestIntegrationEvent();
        await _store.Save(@event);

        // Act — saving the same event a second time should silently deduplicate
        await _store.Save(@event);

        // Assert — only one record persisted
        var count = await _dbContext.InboxMessages.CountAsync();
        count.ShouldBe(1);
    }

    [Fact]
    public async Task GetPending_ReturnsUnprocessedMessages()
    {
        // Arrange
        var event1 = new TestIntegrationEvent { OccurredOn = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var event2 = new TestIntegrationEvent { OccurredOn = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc) };

        await _store.Save(event1);
        await _store.Save(event2);

        // Act
        var pending = await _store.GetPending(10);

        // Assert — ordered by OccurredOnUtc ascending
        pending.Count.ShouldBe(2);
        pending[0].Id.ShouldBe(event1.EventId);
        pending[1].Id.ShouldBe(event2.EventId);
    }

    [Fact]
    public async Task GetPending_ExcludesProcessedMessages()
    {
        // Arrange
        var @event = new TestIntegrationEvent();
        await _store.Save(@event);
        await _store.MarkAsProcessed([@event.EventId]);

        // Act
        var pending = await _store.GetPending(10);

        // Assert
        pending.Count.ShouldBe(0);
    }

    [Fact]
    public async Task HasBeenProcessed_ReturnsTrueOnlyAfterMarkConsumerProcessed()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        const string handlerName = "OrderProcessedHandler";

        // Act + Assert — a live reservation does not count as processed
        (await _store.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeTrue();
        (await _store.HasBeenProcessed(messageId, handlerName)).ShouldBeFalse();

        await _store.MarkConsumerProcessed(messageId, handlerName);
        (await _store.HasBeenProcessed(messageId, handlerName)).ShouldBeTrue();
    }

    [Fact]
    public async Task HasBeenProcessed_ReturnsFalseForUnprocessedMessage()
    {
        // Arrange
        var messageId = Guid.NewGuid();

        // Act
        var result = await _store.HasBeenProcessed(messageId, "AnyHandler");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task TryReserve_DuplicateClaim_SecondCallerLoses()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        const string handlerName = "InvoiceCreatedHandler";

        // Act — a second store (fresh DbContext, same database) races for the same pair
        (await _store.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeTrue();

        using var secondContext = CreateSecondContext();
        var secondStore = new EfInboxStore(secondContext);

        // Assert — the composite PK makes the second claim fail
        (await secondStore.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task TryReserve_ProcessedPair_ReturnsFalseEvenWhenStale()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        const string handlerName = "Handler";
        (await _store.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeTrue();
        await _store.MarkConsumerProcessed(messageId, handlerName);

        // Act — even with a zero timeout (everything stale), a processed pair is never reclaimed
        var result = await _store.TryReserve(messageId, handlerName, TimeSpan.Zero);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task TryReserve_StaleUnprocessedReservation_IsTakenOver()
    {
        // Arrange — a reservation whose owner "crashed" (backdated past the timeout)
        var messageId = Guid.NewGuid();
        const string handlerName = "Handler";
        (await _store.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeTrue();

        await _dbContext.InboxMessageConsumers
            .Where(c => c.InboxMessageId == messageId && c.Name == handlerName)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReservedOnUtc, DateTime.UtcNow.AddMinutes(-10)));

        // Act
        using var secondContext = CreateSecondContext();
        var secondStore = new EfInboxStore(secondContext);
        var takenOver = await secondStore.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5));

        // Assert — takeover succeeded and refreshed the reservation, so a third claim loses
        takenOver.ShouldBeTrue();
        (await _store.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task TryReserve_ConcurrentTakeoverOfStaleReservation_SingleWinner()
    {
        // Arrange — one stale reservation, two takeover attempts
        var messageId = Guid.NewGuid();
        const string handlerName = "Handler";
        (await _store.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeTrue();

        await _dbContext.InboxMessageConsumers
            .Where(c => c.InboxMessageId == messageId && c.Name == handlerName)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReservedOnUtc, DateTime.UtcNow.AddMinutes(-10)));

        // Act — sequential here (Sqlite serializes writes anyway); the winner's update moves
        // ReservedOnUtc past the cutoff, so the second predicate matches zero rows.
        using var contextA = CreateSecondContext();
        using var contextB = CreateSecondContext();
        var first = await new EfInboxStore(contextA).TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5));
        var second = await new EfInboxStore(contextB).TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5));

        // Assert
        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    private sealed record TestIntegrationEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
    }
}
