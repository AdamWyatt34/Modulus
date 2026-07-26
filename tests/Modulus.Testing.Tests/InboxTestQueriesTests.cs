using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;
using Modulus.Testing;
using Shouldly;
using Xunit;

namespace Modulus.Testing.Tests;

// SQLite ":memory:" rather than the EF Core in-memory provider: InboxTestQueries documents this
// as required for reservation-semantics tests (composite primary key + ExecuteUpdateAsync), and
// these read-only queries are written against the same connection style so a test suite can
// share one InboxDbContext setup for both concerns.
public sealed class InboxTestQueriesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public InboxTestQueriesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<InboxDbContext>(options => options.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<InboxDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private async Task SeedAsync(InboxMessage message, params InboxMessageConsumer[] consumers)
    {
        using var scope = _provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxDbContext>();
        dbContext.InboxMessages.Add(message);
        dbContext.InboxMessageConsumers.AddRange(consumers);
        await dbContext.SaveChangesAsync();
    }

    private static InboxMessage Message(DateTime occurredOnUtc) => new()
    {
        Id = Guid.NewGuid(),
        Type = "Test.Event",
        Content = "{}",
        OccurredOnUtc = occurredOnUtc,
    };

    [Fact]
    public async Task GetInboxMessagesAsync_ReturnsRowsOldestFirst()
    {
        var older = Message(DateTime.UtcNow.AddMinutes(-5));
        var newer = Message(DateTime.UtcNow);
        await SeedAsync(older);
        await SeedAsync(newer);

        var all = await _provider.GetInboxMessagesAsync();

        all.Select(m => m.Id).ShouldBe([older.Id, newer.Id]);
    }

    [Fact]
    public async Task HasHandlerProcessedAsync_LiveReservation_ReturnsFalse()
    {
        var message = Message(DateTime.UtcNow);
        var reservation = new InboxMessageConsumer
        {
            InboxMessageId = message.Id,
            Name = "SomeHandler",
            ReservedOnUtc = DateTime.UtcNow,
        };
        await SeedAsync(message, reservation);

        (await _provider.HasHandlerProcessedAsync(message.Id, "SomeHandler")).ShouldBeFalse();
    }

    [Fact]
    public async Task HasHandlerProcessedAsync_MarkedProcessed_ReturnsTrue()
    {
        var message = Message(DateTime.UtcNow);
        var reservation = new InboxMessageConsumer
        {
            InboxMessageId = message.Id,
            Name = "SomeHandler",
            ReservedOnUtc = DateTime.UtcNow,
            ProcessedOnUtc = DateTime.UtcNow,
        };
        await SeedAsync(message, reservation);

        (await _provider.HasHandlerProcessedAsync(message.Id, "SomeHandler")).ShouldBeTrue();
    }

    [Fact]
    public async Task HasHandlerProcessedAsync_UnknownPair_ReturnsFalse()
    {
        (await _provider.HasHandlerProcessedAsync(Guid.NewGuid(), "Nobody")).ShouldBeFalse();
    }
}
