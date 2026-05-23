using Microsoft.EntityFrameworkCore;
using Modulus.Cli.Handlers;
using Modulus.Cli.Tests.Fakes;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Outbox;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Commands;

public class OutboxCommandTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private const string FakeConnection = "Server=test";
    private static OutboxConnection TestConnection => new(FakeConnection, OutboxProvider.SqlServer);

    private OutboxHandler CreateHandler(OutboxDbContext context)
    {
        return new OutboxHandler(_fs, _console, _ => new InMemoryOutboxAdminSession(context));
    }

    private static OutboxDbContext NewInMemoryContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new OutboxDbContext(options);
    }

    private static OutboxMessage NewMessage(int attempts, bool processed = false) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "Sample.SomethingHappened, Sample",
        Payload = "{}",
        CreatedAt = DateTime.UtcNow.AddMinutes(-attempts),
        ProcessedAt = processed ? DateTime.UtcNow : null,
        Attempts = attempts,
        LastError = attempts > 0 ? $"boom #{attempts}" : null,
    };

    [Fact]
    public async Task ListFailed_returns_only_messages_with_attempts_at_or_above_threshold()
    {
        await using var context = NewInMemoryContext(nameof(ListFailed_returns_only_messages_with_attempts_at_or_above_threshold));

        context.OutboxMessages.AddRange(
            NewMessage(attempts: 0),                          // pending
            NewMessage(attempts: 2),                          // in-flight under threshold
            NewMessage(attempts: 5),                          // dead-lettered
            NewMessage(attempts: 6),                          // dead-lettered
            NewMessage(attempts: 5, processed: true));        // processed — excluded
        await context.SaveChangesAsync();

        var handler = CreateHandler(context);
        var exit = await handler.ListFailedAsync(TestConnection, maxAttempts: 5);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.StartsWith("Failed outbox messages (>= 5 attempts): 2"));
    }

    [Fact]
    public async Task ListFailed_with_no_dead_letters_writes_friendly_message()
    {
        await using var context = NewInMemoryContext(nameof(ListFailed_with_no_dead_letters_writes_friendly_message));
        context.OutboxMessages.Add(NewMessage(attempts: 0));
        await context.SaveChangesAsync();

        var handler = CreateHandler(context);
        var exit = await handler.ListFailedAsync(TestConnection, maxAttempts: 5);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain("No failed outbox messages.");
    }

    [Fact]
    public async Task Retry_resets_attempts_and_clears_last_error()
    {
        await using var context = NewInMemoryContext(nameof(Retry_resets_attempts_and_clears_last_error));
        var msg = NewMessage(attempts: 7);
        context.OutboxMessages.Add(msg);
        await context.SaveChangesAsync();

        var handler = CreateHandler(context);
        var exit = await handler.RetryAsync(TestConnection, msg.Id);

        exit.ShouldBe(0);
        var stored = await context.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == msg.Id);
        stored.Attempts.ShouldBe(0);
        stored.LastError.ShouldBeNull();
        _console.SuccessLines.ShouldContain(l => l.Contains(msg.Id.ToString()));
    }

    [Fact]
    public async Task Retry_with_unknown_id_reports_error_and_returns_nonzero()
    {
        await using var context = NewInMemoryContext(nameof(Retry_with_unknown_id_reports_error_and_returns_nonzero));

        var handler = CreateHandler(context);
        var exit = await handler.RetryAsync(TestConnection, Guid.NewGuid());

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    [Fact]
    public async Task Purge_removes_the_message()
    {
        await using var context = NewInMemoryContext(nameof(Purge_removes_the_message));
        var msg = NewMessage(attempts: 7);
        context.OutboxMessages.Add(msg);
        await context.SaveChangesAsync();

        var handler = CreateHandler(context);
        var exit = await handler.PurgeAsync(TestConnection, msg.Id);

        exit.ShouldBe(0);
        (await context.OutboxMessages.AnyAsync(m => m.Id == msg.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task Purge_with_unknown_id_reports_error()
    {
        await using var context = NewInMemoryContext(nameof(Purge_with_unknown_id_reports_error));

        var handler = CreateHandler(context);
        var exit = await handler.PurgeAsync(TestConnection, Guid.NewGuid());

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    [Fact]
    public void ResolveConnection_uses_explicit_connection_string_when_provided()
    {
        var handler = new OutboxHandler(_fs, _console, _ => throw new InvalidOperationException("not invoked"));

        var connection = handler.ResolveConnection("Server=explicit", configPath: null, OutboxProvider.SqlServer);

        connection.ShouldNotBeNull();
        connection!.ConnectionString.ShouldBe("Server=explicit");
        connection.Provider.ShouldBe(OutboxProvider.SqlServer);
    }

    [Fact]
    public void ResolveConnection_falls_back_to_appsettings_messaging_connection_string()
    {
        _fs.SetCurrentDirectory(@"C:\app");
        _fs.SeedFile(@"C:\app\appsettings.json", """{ "Messaging": { "ConnectionString": "Server=from-config" } }""");

        var handler = new OutboxHandler(_fs, _console, _ => throw new InvalidOperationException("not invoked"));

        var connection = handler.ResolveConnection(connectionString: null, configPath: null, OutboxProvider.Sqlite);

        connection.ShouldNotBeNull();
        connection!.ConnectionString.ShouldBe("Server=from-config");
        connection.Provider.ShouldBe(OutboxProvider.Sqlite);
    }

    [Fact]
    public void ResolveConnection_reports_missing_appsettings_and_returns_null()
    {
        _fs.SetCurrentDirectory(@"C:\nowhere");

        var handler = new OutboxHandler(_fs, _console, _ => throw new InvalidOperationException("not invoked"));

        var connection = handler.ResolveConnection(connectionString: null, configPath: null, OutboxProvider.SqlServer);

        connection.ShouldBeNull();
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    private sealed class InMemoryOutboxAdminSession(OutboxDbContext context) : IOutboxAdminSession
    {
        public IOutboxAdminStore Store { get; } = new EfOutboxAdminStore(context);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
