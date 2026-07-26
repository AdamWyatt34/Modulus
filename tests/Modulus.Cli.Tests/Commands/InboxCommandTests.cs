using Modulus.Cli.Handlers;
using Modulus.Cli.Tests.Fakes;
using Modulus.Messaging.Abstractions;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Commands;

public class InboxCommandTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private static OutboxConnection TestConnection => new("Server=test", OutboxProvider.SqlServer);

    private sealed class FakeInboxAdminStore : IInboxAdminStore
    {
        public int CountResult { get; set; }
        public Queue<int> PurgeResults { get; } = new();
        public List<(DateTime OlderThan, int BatchSize)> PurgeCalls { get; } = [];

        public Task<int> CountOldAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(CountResult);

        public Task<int> PurgeOldAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken = default)
        {
            PurgeCalls.Add((olderThanUtc, batchSize));
            return Task.FromResult(PurgeResults.Count > 0 ? PurgeResults.Dequeue() : 0);
        }
    }

    private sealed class FakeInboxAdminSession(IInboxAdminStore store) : IInboxAdminSession
    {
        public IInboxAdminStore Store { get; } = store;
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private InboxHandler CreateHandler(FakeInboxAdminStore store)
        => new(_fs, _console, _ => new FakeInboxAdminSession(store));

    [Fact]
    public async Task Purge_without_confirm_reports_count_and_deletes_nothing()
    {
        var store = new FakeInboxAdminStore { CountResult = 42 };
        var handler = CreateHandler(store);

        var exit = await handler.PurgeAsync(TestConnection, olderThanDays: 7, batchSize: 500, confirm: false);

        exit.ShouldBe(0);
        store.PurgeCalls.ShouldBeEmpty();
        _console.Lines.ShouldContain(l => l.Contains("42 inbox message(s)"));
        _console.Lines.ShouldContain(l => l.Contains("--confirm"));
    }

    [Fact]
    public async Task Purge_with_confirm_repeats_batches_until_drained()
    {
        var store = new FakeInboxAdminStore();
        store.PurgeResults.Enqueue(500);
        store.PurgeResults.Enqueue(500);
        store.PurgeResults.Enqueue(17);
        var handler = CreateHandler(store);

        var exit = await handler.PurgeAsync(TestConnection, olderThanDays: 7, batchSize: 500, confirm: true);

        exit.ShouldBe(0);
        store.PurgeCalls.Count.ShouldBe(3);
        store.PurgeCalls.ShouldAllBe(c => c.BatchSize == 500);
        _console.SuccessLines.ShouldContain(l => l.Contains("1017 inbox message(s)"));
    }

    [Fact]
    public async Task Purge_rejects_older_than_days_below_one()
    {
        var store = new FakeInboxAdminStore();
        var handler = CreateHandler(store);

        var exit = await handler.PurgeAsync(TestConnection, olderThanDays: 0, batchSize: 500, confirm: true);

        exit.ShouldBe(1);
        store.PurgeCalls.ShouldBeEmpty();
        _console.ErrorLines.ShouldContain(l => l.Contains("deduplication window"));
    }

    [Fact]
    public async Task Purge_rejects_out_of_range_batch_size()
    {
        var store = new FakeInboxAdminStore();
        var handler = CreateHandler(store);

        var exit = await handler.PurgeAsync(TestConnection, olderThanDays: 7, batchSize: 0, confirm: true);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("--batch-size"));
    }

    [Fact]
    public async Task Purge_session_factory_failure_reports_friendly_error()
    {
        var handler = new InboxHandler(_fs, _console, _ => throw new InvalidOperationException("bad connection string"));

        var exit = await handler.PurgeAsync(TestConnection, olderThanDays: 7, batchSize: 500, confirm: true);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("bad connection string"));
    }

    [Fact]
    public void ResolveConnection_uses_ConnectionStrings_Default_when_present()
    {
        _fs.SetCurrentDirectory(@"C:\app");
        _fs.SeedFile(@"C:\app\appsettings.json", """{ "ConnectionStrings": { "Default": "Server=default-db" } }""");

        var handler = new InboxHandler(_fs, _console, _ => throw new InvalidOperationException("not invoked"));

        var connection = handler.ResolveConnection(connectionString: null, configPath: null, OutboxProvider.Sqlite);

        connection.ShouldNotBeNull();
        connection!.ConnectionString.ShouldBe("Server=default-db");
    }
}
