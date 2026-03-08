using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

public class EfOutboxStoreTests
{
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(options =>
            options.UseInMemoryDatabase($"OutboxTests_{Guid.NewGuid()}"));
        services.AddScoped<IOutboxStore, EfOutboxStore>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Save_stores_event_as_outbox_message()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        var @event = new TestOrderCreatedEvent
        {
            OrderId = 1,
            CustomerName = "Test"
        };

        await store.Save(@event);

        var messages = await dbContext.OutboxMessages.ToListAsync();
        messages.Count.ShouldBe(1);
        messages[0].Id.ShouldBe(@event.EventId);
        messages[0].EventType.ShouldContain(nameof(TestOrderCreatedEvent));
        messages[0].Payload.ShouldContain("\"OrderId\"");
        messages[0].ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetPending_returns_unprocessed_ordered_by_created()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        var event1 = new TestOrderCreatedEvent
        {
            OrderId = 1,
            CustomerName = "First",
            OccurredOn = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var event2 = new TestOrderCreatedEvent
        {
            OrderId = 2,
            CustomerName = "Second",
            OccurredOn = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        await store.Save(event1);
        await store.Save(event2);

        var pending = await store.GetPending(10);

        pending.Count.ShouldBe(2);
        pending[0].Id.ShouldBe(event1.EventId);
        pending[1].Id.ShouldBe(event2.EventId);
    }

    [Fact]
    public async Task GetPending_respects_batch_size()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        for (var i = 0; i < 5; i++)
        {
            await store.Save(new TestOrderCreatedEvent
            {
                OrderId = i,
                CustomerName = $"Customer {i}"
            });
        }

        var pending = await store.GetPending(2);
        pending.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetPending_excludes_processed_messages()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        var @event = new TestOrderCreatedEvent
        {
            OrderId = 1,
            CustomerName = "Test"
        };

        await store.Save(@event);

        // Mark as processed directly via DbContext since ExecuteUpdateAsync
        // is not supported by the EF Core InMemory provider
        var message = await dbContext.OutboxMessages.FirstAsync(m => m.Id == @event.EventId);
        message.ProcessedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        var pending = await store.GetPending(10);
        pending.Count.ShouldBe(0);
    }
}
