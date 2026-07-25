using System.Text.RegularExpressions;
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

    /// <summary>All rows regardless of NextAttemptOnUtc backoff eligibility — GetPending
    /// (and the helper above) deliberately excludes backed-off rows, so assertions about a
    /// row's own Attempts/LastError/NextAttemptOnUtc must read the table directly instead.</summary>
    private static async Task<IReadOnlyList<OutboxMessage>> GetAllRows(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        return await dbContext.OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync();
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
    public async Task DispatchPending_UnknownEventType_MarksFailedInsteadOfLeavingAttemptsAtZero()
    {
        // C3: before the fix, the unknown-type skip path bare-`continue`d without ever calling
        // MarkAsFailed, so the row's Attempts stayed 0 forever — invisible to
        // `modulus outbox list-failed` (which filters on Attempts >= maxAttempts) and eligible
        // to be refetched every single pass.
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
        var progressed = await dispatcher.DispatchPendingAsync();

        transport.Published.ShouldBeEmpty();
        progressed.ShouldBe(1); // the poison row still counts as forward progress this pass

        var rows = await GetAllRows(provider);
        rows.Count.ShouldBe(1);
        rows[0].ProcessedAt.ShouldBeNull();
        rows[0].Attempts.ShouldBe(1);
        rows[0].LastError.ShouldNotBeNullOrEmpty();
        rows[0].LastError.ShouldContain("NonExistent.Type, NonExistent.Assembly");
        // Backed off, not immediately eligible again — this is what breaks the hot loop.
        rows[0].NextAttemptOnUtc.ShouldNotBeNull();
        rows[0].NextAttemptOnUtc!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task DispatchPending_PayloadDeserializesToNull_MarksFailedInsteadOfLeavingAttemptsAtZero()
    {
        // Same C3 poison-row defect, the other skip path: a known event type whose payload
        // deserializes to a valid-JSON-but-null value.
        var transport = new FakeMessageTransport();
        using var provider = BuildProvider(transport);

        using (var seedScope = provider.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = typeof(TestOrderCreatedEvent).AssemblyQualifiedName!,
                Payload = "null",
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        var progressed = await dispatcher.DispatchPendingAsync();

        transport.Published.ShouldBeEmpty();
        progressed.ShouldBe(1);

        var rows = await GetAllRows(provider);
        rows[0].Attempts.ShouldBe(1);
        rows[0].LastError.ShouldNotBeNullOrEmpty();
        rows[0].NextAttemptOnUtc.ShouldNotBeNull();
        rows[0].NextAttemptOnUtc!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task DispatchPending_UnknownEventTypeAssemblyVersionBump_StillResolvesViaNormalizedMatch()
    {
        // C3 version-insensitive allowlist: a row saved under an older assembly version must
        // still resolve once the exact AssemblyQualifiedName no longer matches (a deploy that
        // only bumps Version/Culture/PublicKeyToken must not orphan in-flight rows).
        var transport = new FakeMessageTransport();
        using var provider = BuildProvider(transport);

        var realAqn = typeof(TestOrderCreatedEvent).AssemblyQualifiedName!;
        var staleAqn = Regex.Replace(realAqn, @",\s*Version=[^,\]]*", ", Version=0.0.0.0");

        // Sanity-check the fixture actually changed the AQN, or this test would pass vacuously.
        staleAqn.ShouldNotBe(realAqn);

        using (var seedScope = provider.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = staleAqn,
                Payload = """{"OrderId":42,"CustomerName":"Stale Version"}""",
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        var progressed = await dispatcher.DispatchPendingAsync();

        progressed.ShouldBe(1);
        transport.Published.Count.ShouldBe(1);
        transport.Published.TryPeek(out var envelope).ShouldBeTrue();
        envelope!.MessageType.ShouldBe(typeof(TestOrderCreatedEvent).FullName);

        (await GetAllRows(provider))[0].ProcessedAt.ShouldNotBeNull();
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
        var progressed = await dispatcher.DispatchPendingAsync();

        progressed.ShouldBe(1);
        var rows = await GetAllRows(provider);
        rows.Count.ShouldBe(1);
        rows[0].Attempts.ShouldBe(1);
        rows[0].LastError.ShouldBe("broker unavailable");
        rows[0].NextAttemptOnUtc.ShouldNotBeNull();
        rows[0].NextAttemptOnUtc!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task DispatchPending_TransportRecovers_RetriedMessageIsPublished()
    {
        var transport = new FakeMessageTransport
        {
            PublishFailure = new InvalidOperationException("transient"),
        };
        // Zero backoff: this test drives two dispatch passes back-to-back with no delay, so a
        // non-zero RetryPolicy would leave the row's NextAttemptOnUtc in the future and the
        // second pass would not refetch it — that scenario is covered separately by the
        // starvation-regression test below.
        using var provider = BuildProvider(transport, options =>
        {
            options.RetryPolicy.InitialInterval = TimeSpan.Zero;
            options.RetryPolicy.IntervalIncrement = TimeSpan.Zero;
        });
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
            // Zero backoff so the second immediate call is not itself excluded by
            // NextAttemptOnUtc before Attempts ever reaches the dead-letter threshold.
            options.RetryPolicy.InitialInterval = TimeSpan.Zero;
            options.RetryPolicy.IntervalIncrement = TimeSpan.Zero;
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
    public async Task DispatchPending_FullBatchOfPoisonRows_DoesNotStarveNewerKnownRow()
    {
        // C3 / H-MSG3 starvation + hot-loop regression: a full OutboxBatchSize of poison rows
        // must not wedge the queue head forever, and once they are backed off,
        // DispatchPendingAsync must not keep reporting a full-batch "drain forever" count.
        const int batchSize = 2;
        var transport = new FakeMessageTransport();
        using var provider = BuildProvider(transport, options =>
        {
            options.OutboxBatchSize = batchSize;
            // Long enough that the very next, immediate pass reliably falls before the
            // poison rows' computed NextAttemptOnUtc. MaxInterval must be raised to match, or
            // ValidateRetryPolicy rejects MaxInterval < InitialInterval at registration time.
            options.RetryPolicy.InitialInterval = TimeSpan.FromMinutes(5);
            options.RetryPolicy.MaxInterval = TimeSpan.FromMinutes(5);
        });

        using (var seedScope = provider.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var baseTime = DateTime.UtcNow.AddMinutes(-10);
            for (var i = 0; i < batchSize; i++)
            {
                dbContext.OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = $"NonExistent.Type{i}, NonExistent.Assembly",
                    Payload = "{}",
                    CreatedAt = baseTime.AddSeconds(i),
                });
            }
            await dbContext.SaveChangesAsync();
        }

        // Seeded after (so newer than) the poison rows: before this fix, a full batch of
        // poison rows ahead of it in CreatedAt order would keep matching every pass forever,
        // so this row would never be reached no matter how many passes ran.
        await SeedEvent(provider, orderId: 99);

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();

        var firstPass = await dispatcher.DispatchPendingAsync();
        firstPass.ShouldBe(batchSize); // both poison rows made forward progress (marked failed)
        transport.Published.ShouldBeEmpty();

        var secondPass = await dispatcher.DispatchPendingAsync();

        // The poison rows are now backed off ~5 minutes out, so this pass is not another
        // full-batch drain of the same two rows — it reaches the newer, valid row instead.
        secondPass.ShouldBe(1);
        transport.Published.Count.ShouldBe(1);
        transport.Published.TryPeek(out var envelope).ShouldBeTrue();
        envelope!.MessageType.ShouldBe(typeof(TestOrderCreatedEvent).FullName);

        // Nothing left to do: proves the loop terminates instead of hot-spinning.
        (await dispatcher.DispatchPendingAsync()).ShouldBe(0);
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
