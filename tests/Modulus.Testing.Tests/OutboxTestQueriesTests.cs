using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Modulus.Testing;
using Shouldly;
using Xunit;

namespace Modulus.Testing.Tests;

// The EF Core in-memory provider is sufficient here: none of these queries depend on
// transactions, raw SQL, or constraint enforcement (see InboxTestQueriesTests for the contrast).
public class OutboxTestQueriesTests
{
    private static ServiceProvider BuildProvider()
    {
        // The database name/root must be captured once here, outside the AddDbContext options
        // delegate: that delegate's default lifetime is Scoped, so it re-runs (with a fresh
        // Guid) every time a scope resolves a new OutboxDbContext. Extension methods each open
        // their own scope, so without this capture, seeding and querying would silently talk to
        // different in-memory databases.
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();

        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(options => options.UseInMemoryDatabase(databaseName, root));
        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(IServiceProvider provider, params OutboxMessage[] messages)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        dbContext.OutboxMessages.AddRange(messages);
        await dbContext.SaveChangesAsync();
    }

    private static OutboxMessage Message(
        DateTime createdAt,
        DateTime? processedAt = null,
        int attempts = 0,
        DateTime? nextAttemptOnUtc = null,
        DateTime? scheduledOnUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "Test.Event",
        Payload = "{}",
        CreatedAt = createdAt,
        ProcessedAt = processedAt,
        Attempts = attempts,
        NextAttemptOnUtc = nextAttemptOnUtc,
        ScheduledOnUtc = scheduledOnUtc,
    };

    [Fact]
    public async Task GetOutboxMessagesAsync_ReturnsEveryRow_OldestFirst()
    {
        await using var provider = BuildProvider();
        var older = Message(DateTime.UtcNow.AddMinutes(-10));
        var newer = Message(DateTime.UtcNow);
        await SeedAsync(provider, older, newer);

        var all = await provider.GetOutboxMessagesAsync();

        all.Select(m => m.Id).ShouldBe([older.Id, newer.Id]);
    }

    [Fact]
    public async Task GetPendingOutboxMessagesAsync_ExcludesProcessedDeadLetteredBackingOffAndFutureRows()
    {
        await using var provider = BuildProvider();
        var pending = Message(DateTime.UtcNow, attempts: 1);
        var processed = Message(DateTime.UtcNow, processedAt: DateTime.UtcNow);
        var deadLettered = Message(DateTime.UtcNow, attempts: 5);
        var backingOff = Message(DateTime.UtcNow, attempts: 1, nextAttemptOnUtc: DateTime.UtcNow.AddMinutes(5));
        var futureScheduled = Message(DateTime.UtcNow, scheduledOnUtc: DateTime.UtcNow.AddMinutes(5));
        await SeedAsync(provider, pending, processed, deadLettered, backingOff, futureScheduled);

        var result = await provider.GetPendingOutboxMessagesAsync(maxAttempts: 5);

        result.ShouldHaveSingleItem().Id.ShouldBe(pending.Id);
    }

    [Fact]
    public async Task GetDeadLetteredOutboxMessagesAsync_ReturnsOnlyRowsAtOrAboveMaxAttempts()
    {
        await using var provider = BuildProvider();
        var pending = Message(DateTime.UtcNow, attempts: 1);
        var deadLettered = Message(DateTime.UtcNow, attempts: 5);
        await SeedAsync(provider, pending, deadLettered);

        var result = await provider.GetDeadLetteredOutboxMessagesAsync(maxAttempts: 5);

        result.ShouldHaveSingleItem().Id.ShouldBe(deadLettered.Id);
    }

    [Fact]
    public async Task WaitForOutboxDrainAsync_ReturnsOnceThePendingRowIsMarkedProcessed()
    {
        await using var provider = BuildProvider();
        var message = Message(DateTime.UtcNow);
        await SeedAsync(provider, message);

        var draining = provider.WaitForOutboxDrainAsync(TimeSpan.FromSeconds(2));

        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var tracked = await dbContext.OutboxMessages.FirstAsync(m => m.Id == message.Id);
            tracked.ProcessedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        await draining;
    }

    [Fact]
    public async Task WaitForOutboxDrainAsync_NeverDrains_ThrowsTimeoutException()
    {
        await using var provider = BuildProvider();
        await SeedAsync(provider, Message(DateTime.UtcNow));

        await Should.ThrowAsync<TimeoutException>(
            () => provider.WaitForOutboxDrainAsync(TimeSpan.FromMilliseconds(150)));
    }
}
