using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests;

// Drives the real IOutboxDispatcher for single synchronous dispatch passes against a
// FakeMessageTransport — no BackgroundService lifetime, no broker, no waits.
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

    private ServiceProvider BuildProvider(
        FakeMessageTransport transport,
        Action<MessagingOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(_connection));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
            configureOptions?.Invoke(options);
        });

        // Last registration wins: the dispatcher publishes to the fake.
        services.AddSingleton<IMessageTransport>(transport);

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreated();

        return provider;
    }

    private static async Task SeedEvent(ServiceProvider provider, int orderId = 77)
    {
        using var scope = provider.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        await outboxStore.Save(new TestOrderCreatedEvent
        {
            OrderId = orderId,
            CustomerName = $"Customer {orderId}"
        });
    }

    private static async Task<IReadOnlyList<Abstractions.OutboxMessage>> GetPending(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        return await outboxStore.GetPending(100, int.MaxValue);
    }

    [Fact]
    public async Task DispatchPending_PendingMessage_PublishesEnvelopeAndMarksProcessed()
    {
        var transport = new FakeMessageTransport();
        using var provider = BuildProvider(transport);
        await SeedEvent(provider, orderId: 77);

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.DispatchPendingAsync();

        transport.Published.Count.ShouldBe(1);
        transport.Published.TryPeek(out var envelope).ShouldBeTrue();
        envelope!.MessageType.ShouldBe(typeof(TestOrderCreatedEvent).FullName);
        envelope.MessageId.ShouldNotBe(Guid.Empty);

        (await GetPending(provider)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DispatchPending_UnknownEventType_SkipsAndLeavesPending()
    {
        var transport = new FakeMessageTransport();
        using var provider = BuildProvider(transport);

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

        transport.Published.ShouldBeEmpty();
        var stillPending = await GetPending(provider);
        stillPending.Count.ShouldBe(1);
        stillPending[0].Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task DispatchPending_TransportFails_MarksFailedWithErrorAndIncrementsAttempts()
    {
        var transport = new FakeMessageTransport
        {
            PublishFailure = new InvalidOperationException("broker unavailable"),
        };
        using var provider = BuildProvider(transport);
        await SeedEvent(provider);

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.DispatchPendingAsync();

        var stillPending = await GetPending(provider);
        stillPending.Count.ShouldBe(1);
        stillPending[0].Attempts.ShouldBe(1);
        stillPending[0].LastError.ShouldBe("broker unavailable");
    }

    [Fact]
    public async Task DispatchPending_TransportRecovers_RetriedMessageIsPublished()
    {
        var transport = new FakeMessageTransport
        {
            PublishFailure = new InvalidOperationException("transient"),
        };
        using var provider = BuildProvider(transport);
        await SeedEvent(provider);

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.DispatchPendingAsync();

        transport.PublishFailure = null;
        await dispatcher.DispatchPendingAsync();

        transport.Published.Count.ShouldBe(1);
        (await GetPending(provider)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DispatchPending_AttemptsAtThreshold_MessageNoLongerFetched()
    {
        var transport = new FakeMessageTransport
        {
            PublishFailure = new InvalidOperationException("permanent"),
        };
        using var provider = BuildProvider(transport, options =>
        {
            options.RetryPolicy.MaxAttempts = 2;
        });
        await SeedEvent(provider);

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.DispatchPendingAsync(); // attempt 1
        await dispatcher.DispatchPendingAsync(); // attempt 2 -> dead-letter threshold

        transport.PublishFailure = null;
        await dispatcher.DispatchPendingAsync(); // no longer fetched: Attempts >= MaxAttempts

        transport.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task DispatchPending_BatchSizeRespected_ProcessesOnlyBatch()
    {
        var transport = new FakeMessageTransport();
        using var provider = BuildProvider(transport, options => options.OutboxBatchSize = 2);

        for (var i = 1; i <= 3; i++)
            await SeedEvent(provider, orderId: i);

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.DispatchPendingAsync();

        transport.Published.Count.ShouldBe(2);
        (await GetPending(provider)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task DispatchPending_EndToEnd_InMemoryTransportDeliversToHandler()
    {
        // Full outbox -> transport -> consumer pipeline -> handler roundtrip using the
        // real in-memory transport and hosted services.
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(_connection));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });
        services.AddSingleton<IIntegrationEventHandler<TestOrderCreatedEvent>>(handler);

        using (var schemaProvider = services.BuildServiceProvider())
        using (var scope = schemaProvider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreated();
        }

        await using var harness = await MessagingTestHarness.StartAsync(services);
        // No manual dispatch: Save's wake signal must drive the hosted OutboxProcessor
        // (dispatching here as well would double-publish — the processor is the single
        // writer; a second concurrent dispatch pass violates that assumption).
        await SeedEvent(harness.Provider, orderId: 7);

        await TestWait.WaitForConditionAsync(
            () => handler.HandledEvents.Count >= 1,
            timeout: TimeSpan.FromSeconds(10),
            because: "the outbox wake signal should dispatch the row without waiting for the poll interval");
        handler.HandledEvents[0].OrderId.ShouldBe(7);
    }
}
