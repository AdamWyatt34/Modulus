using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

// Drives the real IOutboxDispatcher (extracted from OutboxProcessor) for a single
// synchronous dispatch pass — no BackgroundService lifetime to race against.
// Uses Sqlite in-memory because EfOutboxStore.MarkAsProcessed relies on
// ExecuteUpdateAsync, which the EF Core InMemory provider does not support.
public sealed class OutboxDispatcherTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public OutboxDispatcherTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private ServiceProvider BuildProvider(TestOrderCreatedHandler? handler = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(_connection));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });

        if (handler is not null)
            services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreated();

        return provider;
    }

    [Fact]
    public async Task DispatchPending_PendingMessage_PublishesAndMarksProcessed()
    {
        var handler = new TestOrderCreatedHandler();
        using var provider = BuildProvider(handler);

        using (var seedScope = provider.CreateScope())
        {
            var outboxStore = seedScope.ServiceProvider.GetRequiredService<IOutboxStore>();
            await outboxStore.Save(new TestOrderCreatedEvent
            {
                OrderId = 77,
                CustomerName = "Outbox Test"
            });
        }

        var busControl = provider.GetRequiredService<MassTransit.IBusControl>();
        await busControl.StartAsync();

        try
        {
            var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync();

            await TestWait.WaitForConditionAsync(
                () => handler.HandledEvents.Count == 1,
                because: "the dispatched event should reach the handler");

            handler.HandledEvents[0].OrderId.ShouldBe(77);

            using var scope = provider.CreateScope();
            var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var stillPending = await outboxStore.GetPending(100, int.MaxValue);
            stillPending.ShouldBeEmpty();
        }
        finally
        {
            await busControl.StopAsync();
        }
    }

    [Fact]
    public async Task DispatchPending_UnknownEventType_SkipsAndLeavesPending()
    {
        using var provider = BuildProvider();

        using (var seedScope = provider.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "NonExistent.Type, NonExistent.Assembly",
                Payload = "{}",
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.DispatchPendingAsync();

        using var scope = provider.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var stillPending = await outboxStore.GetPending(100, int.MaxValue);
        stillPending.Count.ShouldBe(1);
        stillPending[0].Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task DispatchPending_BatchSizeRespected_ProcessesOnlyBatch()
    {
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(_connection));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
            options.OutboxBatchSize = 2;
        });
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);
        using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            dbContext.Database.EnsureCreated();
            var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            for (var i = 1; i <= 3; i++)
                await outboxStore.Save(new TestOrderCreatedEvent { OrderId = i, CustomerName = $"C{i}" });
        }

        var busControl = provider.GetRequiredService<MassTransit.IBusControl>();
        await busControl.StartAsync();

        try
        {
            var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync();

            using var scope = provider.CreateScope();
            var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var stillPending = await outboxStore.GetPending(100, int.MaxValue);
            stillPending.Count.ShouldBe(1);
        }
        finally
        {
            await busControl.StopAsync();
        }
    }
}
