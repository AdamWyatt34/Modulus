using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Inbox;

public class EfInboxStoreTests : IDisposable
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

    [Fact]
    public async Task Save_stores_event_in_inbox()
    {
        var @event = new TestIntegrationEvent();

        await _store.Save(@event);

        var messages = await _dbContext.InboxMessages.ToListAsync();
        messages.Count.ShouldBe(1);
        messages[0].Id.ShouldBe(@event.EventId);
    }

    [Fact]
    public async Task Save_ignores_duplicate_events()
    {
        var @event = new TestIntegrationEvent();

        await _store.Save(@event);
        await _store.Save(@event);

        var count = await _dbContext.InboxMessages.CountAsync();
        count.ShouldBe(1);
    }

    [Fact]
    public async Task GetPending_returns_unprocessed_messages()
    {
        var @event = new TestIntegrationEvent();
        await _store.Save(@event);

        var pending = await _store.GetPending(10);

        pending.Count.ShouldBe(1);
    }

    [Fact]
    public async Task MarkAsProcessed_sets_processed_timestamp()
    {
        var @event = new TestIntegrationEvent();
        await _store.Save(@event);

        await _store.MarkAsProcessed([@event.EventId]);

        var message = await _dbContext.InboxMessages.FindAsync(@event.EventId);
        message!.ProcessedOnUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task HasBeenProcessed_returns_false_when_not_consumed()
    {
        var result = await _store.HasBeenProcessed(Guid.NewGuid(), "SomeHandler");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RecordConsumer_and_HasBeenProcessed_round_trip()
    {
        var messageId = Guid.NewGuid();
        var handlerName = "TestHandler";

        await _store.RecordConsumer(messageId, handlerName);

        var result = await _store.HasBeenProcessed(messageId, handlerName);
        result.ShouldBeTrue();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private sealed record TestIntegrationEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
    }
}
