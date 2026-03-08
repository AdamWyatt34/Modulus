using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Inbox;

public sealed class EfInboxStoreTests : IDisposable
{
    private readonly InboxDbContext _dbContext;
    private readonly EfInboxStore _store;

    public EfInboxStoreTests()
    {
        var options = new DbContextOptionsBuilder<InboxDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new InboxDbContext(options);
        _store = new EfInboxStore(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Save_ValidMessage_PersistsToDatabase()
    {
        // Arrange
        var @event = new TestIntegrationEvent();

        // Act
        await _store.Save(@event);

        // Assert
        var messages = await _dbContext.InboxMessages.ToListAsync();
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

        // Mark as processed directly via DbContext since ExecuteUpdateAsync
        // is not supported by the EF Core InMemory provider
        var message = await _dbContext.InboxMessages.FindAsync(@event.EventId);
        message!.ProcessedOnUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var pending = await _store.GetPending(10);

        // Assert
        pending.Count.ShouldBe(0);
    }

    [Fact]
    public async Task HasBeenProcessed_ReturnsTrueForProcessedMessage()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        const string handlerName = "OrderProcessedHandler";

        // Act
        await _store.RecordConsumer(messageId, handlerName);
        var result = await _store.HasBeenProcessed(messageId, handlerName);

        // Assert
        result.ShouldBeTrue();
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
    public async Task RecordConsumer_PersistsConsumerRecord()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        const string handlerName = "InvoiceCreatedHandler";

        // Act
        await _store.RecordConsumer(messageId, handlerName);

        // Assert — EF InMemory does not enforce FK constraints, assert state directly
        var consumers = await _dbContext.InboxMessageConsumers.ToListAsync();
        consumers.Count.ShouldBe(1);
        consumers[0].InboxMessageId.ShouldBe(messageId);
        consumers[0].Name.ShouldBe(handlerName);
    }

    private sealed record TestIntegrationEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
    }
}
