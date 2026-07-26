using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

// Sqlite in-memory rather than the EF InMemory provider: ClaimPending/MarkAsProcessed/MarkAsFailed
// all rely on ExecuteUpdateAsync, which the EF Core InMemory provider does not support.
public sealed class EfOutboxStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public EfOutboxStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private ServiceProvider CreateProvider(FakeOutboxNotifier? notifier = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IOutboxNotifier>(notifier ?? new FakeOutboxNotifier());
        services.AddScoped<IOutboxStore, EfOutboxStore>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreated();

        return provider;
    }

    private static Task<IReadOnlyList<OutboxMessage>> ClaimAll(IOutboxStore store, int batchSize = 10, int maxAttempts = int.MaxValue)
        => store.ClaimPending($"test-{Guid.NewGuid():N}", TimeSpan.FromMinutes(5), batchSize, maxAttempts);

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
        messages[0].ClaimedBy.ShouldBeNull();
        messages[0].ClaimedUntil.ShouldBeNull();
    }

    [Fact]
    public async Task Save_WithoutTransaction_Notifies()
    {
        var notifier = new FakeOutboxNotifier();
        using var provider = CreateProvider(notifier);
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        await store.Save(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "Test" });

        notifier.NotifyCount.ShouldBe(1);
    }

    [Fact]
    public async Task ClaimPending_returns_unprocessed_ordered_by_created()
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

        var pending = await ClaimAll(store);

        pending.Count.ShouldBe(2);
        pending[0].Id.ShouldBe(event1.EventId);
        pending[1].Id.ShouldBe(event2.EventId);
        pending[0].ClaimedBy.ShouldNotBeNull();
        pending[0].ClaimedUntil.ShouldNotBeNull();
    }

    [Fact]
    public async Task ClaimPending_respects_batch_size()
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

        var pending = await ClaimAll(store, batchSize: 2);
        pending.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ClaimPending_excludes_processed_messages()
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

        var message = await dbContext.OutboxMessages.FirstAsync(m => m.Id == @event.EventId);
        message.ProcessedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        var pending = await ClaimAll(store);
        pending.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CountPending_counts_only_unprocessed_below_max_attempts()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        await store.Save(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "Pending" });
        await store.Save(new TestOrderCreatedEvent { OrderId = 2, CustomerName = "Processed" });
        await store.Save(new TestOrderCreatedEvent { OrderId = 3, CustomerName = "DeadLettered" });

        var messages = await dbContext.OutboxMessages.OrderBy(m => m.CreatedAt).ToListAsync();
        messages[1].ProcessedAt = DateTime.UtcNow;
        messages[2].Attempts = 5;
        await dbContext.SaveChangesAsync();

        (await store.CountPending(maxAttempts: 5)).ShouldBe(1);
    }

    [Fact]
    public async Task CountPending_includes_claimed_but_unprocessed_rows()
    {
        // Claimed-but-unprocessed is still outstanding work: the backlog health check must not
        // under-report just because another (or this) instance currently holds the claim.
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        await store.Save(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "Test" });
        var claimed = await store.ClaimPending("owner-a", TimeSpan.FromMinutes(5), 10, int.MaxValue);
        claimed.Count.ShouldBe(1);

        (await store.CountPending(maxAttempts: 5)).ShouldBe(1);
    }
}
