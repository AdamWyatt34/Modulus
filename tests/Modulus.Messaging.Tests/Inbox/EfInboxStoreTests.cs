using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    private sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw new DbUpdateException("Simulated failure unrelated to a duplicate row (e.g. a timeout or deadlock).");
    }

    [Fact]
    public async Task Save_DbUpdateExceptionForReasonOtherThanDuplicate_Rethrows()
    {
        // EfInboxStore.cs:32-36 (pre-fix) swallowed every DbUpdateException as "a concurrent
        // duplicate save", including timeouts and deadlocks that have nothing to do with the
        // row already existing. The interceptor throws before SaveChangesAsync ever reaches
        // the database, so the row is guaranteed to still be missing afterward — Save must
        // see that via its re-check and rethrow instead of silently swallowing it.
        var options = new DbContextOptionsBuilder<InboxDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new ThrowingSaveChangesInterceptor())
            .Options;
        using var throwingContext = new InboxDbContext(options);
        var throwingStore = new EfInboxStore(throwingContext);

        var @event = new TestIntegrationEvent();

        await Should.ThrowAsync<DbUpdateException>(() => throwingStore.Save(@event));

        (await _dbContext.InboxMessages.AsNoTracking().AnyAsync(m => m.Id == @event.EventId)).ShouldBeFalse();
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

    [Fact]
    public async Task ReleaseReservation_UnprocessedReservation_AllowsImmediateReReservation()
    {
        // H-MSG4: releasing must let a replay reserve right away — not merely age the
        // reservation until ConsumerReservationTimeout elapses.
        var messageId = Guid.NewGuid();
        const string handlerName = "Handler";
        (await _store.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeTrue();

        await _store.ReleaseReservation(messageId, handlerName);

        (await _store.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task ReleaseReservation_ProcessedPair_DoesNotUndoCompletion()
    {
        // A completed reservation must never be un-done, even if release is called on it
        // (e.g. a caller racing its own success against a cancellation-driven cleanup path).
        var messageId = Guid.NewGuid();
        const string handlerName = "Handler";
        (await _store.TryReserve(messageId, handlerName, TimeSpan.FromMinutes(5))).ShouldBeTrue();
        await _store.MarkConsumerProcessed(messageId, handlerName);

        await _store.ReleaseReservation(messageId, handlerName);

        (await _store.HasBeenProcessed(messageId, handlerName)).ShouldBeTrue();
    }

    [Fact]
    public async Task ReleaseReservation_NoReservationExists_DoesNotThrow()
    {
        await Should.NotThrowAsync(() => _store.ReleaseReservation(Guid.NewGuid(), "NeverReserved"));
    }

    private sealed record TestIntegrationEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
    }
}
