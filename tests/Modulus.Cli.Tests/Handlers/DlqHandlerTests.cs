using Modulus.Cli.Handlers;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class DlqHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private sealed class FakeDlqBrowser : IDlqBrowser
    {
        public List<DlqMessage> Messages { get; } = [];
        public Exception? Failure { get; set; }
        public List<string> ReplayedIds { get; } = [];
        public int ReplayAllCalls { get; private set; }
        public bool Disposed { get; private set; }

        public Task<IReadOnlyList<DlqMessage>> ListAsync(int max, CancellationToken cancellationToken = default)
            => Failure is not null
                ? throw Failure
                : Task.FromResult<IReadOnlyList<DlqMessage>>(Messages.Take(max).ToList());

        public Task<bool> ReplayAsync(string messageId, int max, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                throw Failure;

            var found = Messages.Any(m => m.MessageId == messageId);
            if (found)
                ReplayedIds.Add(messageId);
            return Task.FromResult(found);
        }

        public Task<int> ReplayAllAsync(int max, CancellationToken cancellationToken = default)
        {
            ReplayAllCalls++;
            return Task.FromResult(Math.Min(Messages.Count, max));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static readonly DlqConnection RabbitConnection =
        new(DlqTransport.RabbitMq, "amqp://localhost", "checkout", null);

    private DlqHandler CreateHandler(FakeDlqBrowser browser)
        => new(_fs, _console, _ => browser);

    [Fact]
    public async Task List_empty_queue_reports_and_returns_zero()
    {
        var browser = new FakeDlqBrowser();
        var handler = CreateHandler(browser);

        var exit = await handler.ListAsync(RabbitConnection, max: 50);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("No dead-lettered messages"));
        browser.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task List_renders_table_rows()
    {
        var browser = new FakeDlqBrowser();
        browser.Messages.Add(new DlqMessage("id-1", "MyApp.Orders.OrderPlacedEvent", DateTimeOffset.UtcNow, "RetriesExhausted", 3));
        var handler = CreateHandler(browser);

        var exit = await handler.ListAsync(RabbitConnection, max: 50);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("id-1") && l.Contains("OrderPlacedEvent") && l.Contains("RetriesExhausted"));
    }

    [Fact]
    public async Task List_broker_failure_reports_error()
    {
        var browser = new FakeDlqBrowser { Failure = new InvalidOperationException("connection refused") };
        var handler = CreateHandler(browser);

        var exit = await handler.ListAsync(RabbitConnection, max: 50);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(e => e.Contains("connection refused"));
    }

    [Fact]
    public async Task Replay_by_id_succeeds_when_found()
    {
        var browser = new FakeDlqBrowser();
        browser.Messages.Add(new DlqMessage("id-7", "Event", null, null, 1));
        var handler = CreateHandler(browser);

        var exit = await handler.ReplayAsync(RabbitConnection, "id-7", all: false, max: 50);

        exit.ShouldBe(0);
        browser.ReplayedIds.ShouldBe(["id-7"]);
        _console.SuccessLines.ShouldContain(l => l.Contains("id-7"));
    }

    [Fact]
    public async Task Replay_by_id_not_found_returns_one()
    {
        var browser = new FakeDlqBrowser();
        var handler = CreateHandler(browser);

        var exit = await handler.ReplayAsync(RabbitConnection, "missing", all: false, max: 50);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(e => e.Contains("missing"));
    }

    [Fact]
    public async Task Replay_all_reports_count()
    {
        var browser = new FakeDlqBrowser();
        browser.Messages.Add(new DlqMessage("a", "E", null, null, 1));
        browser.Messages.Add(new DlqMessage("b", "E", null, null, 1));
        var handler = CreateHandler(browser);

        var exit = await handler.ReplayAsync(RabbitConnection, messageId: null, all: true, max: 50);

        exit.ShouldBe(0);
        browser.ReplayAllCalls.ShouldBe(1);
        _console.SuccessLines.ShouldContain(l => l.Contains("Replayed 2 message(s)"));
    }

    [Fact]
    public async Task Replay_requires_exactly_one_of_id_or_all()
    {
        var handler = CreateHandler(new FakeDlqBrowser());

        (await handler.ReplayAsync(RabbitConnection, messageId: null, all: false, max: 50)).ShouldBe(1);
        (await handler.ReplayAsync(RabbitConnection, "id", all: true, max: 50)).ShouldBe(1);
        _console.ErrorLines.Count(e => e.Contains("--message-id") && e.Contains("--all")).ShouldBe(2);
    }

    // ── Connection resolution ─────────────────────────────────────

    private const string AppSettings =
        """
        {
          "Messaging": {
            "Transport": "RabbitMq",
            "ConnectionString": "amqp://user:pass@broker:5672",
            "EndpointName": "checkout"
          }
        }
        """;

    [Fact]
    public void ResolveConnection_prefers_explicit_options()
    {
        var handler = CreateHandler(new FakeDlqBrowser());

        var connection = handler.ResolveConnection(
            DlqTransport.RabbitMq, "amqp://explicit", null, "explicit-endpoint", null);

        connection.ShouldNotBeNull();
        connection.ConnectionString.ShouldBe("amqp://explicit");
        connection.EndpointName.ShouldBe("explicit-endpoint");
    }

    [Fact]
    public void ResolveConnection_falls_back_to_appsettings()
    {
        _fs.SetCurrentDirectory(@"C:\app");
        _fs.SeedFile(@"C:\app\appsettings.json", AppSettings);
        var handler = CreateHandler(new FakeDlqBrowser());

        var connection = handler.ResolveConnection(DlqTransport.RabbitMq, null, null, null, null);

        connection.ShouldNotBeNull();
        connection.ConnectionString.ShouldBe("amqp://user:pass@broker:5672");
        connection.EndpointName.ShouldBe("checkout");
    }

    [Fact]
    public void ResolveConnection_missing_endpoint_fails_with_guidance()
    {
        _fs.SetCurrentDirectory(@"C:\app");
        _fs.SeedFile(@"C:\app\appsettings.json", """{ "Messaging": { "ConnectionString": "amqp://x" } }""");
        var handler = CreateHandler(new FakeDlqBrowser());

        var connection = handler.ResolveConnection(DlqTransport.RabbitMq, null, null, null, null);

        connection.ShouldBeNull();
        _console.ErrorLines.ShouldContain(e => e.Contains("--endpoint"));
    }

    [Fact]
    public void ResolveConnection_asb_requires_event_type()
    {
        var handler = CreateHandler(new FakeDlqBrowser());

        var connection = handler.ResolveConnection(
            DlqTransport.AzureServiceBus, "Endpoint=sb://ns", null, "checkout", eventTypeName: null);

        connection.ShouldBeNull();
        _console.ErrorLines.ShouldContain(e => e.Contains("--event"));
    }

    [Fact]
    public void ResolveConnection_missing_config_file_fails()
    {
        _fs.SetCurrentDirectory(@"C:\app");
        var handler = CreateHandler(new FakeDlqBrowser());

        var connection = handler.ResolveConnection(DlqTransport.RabbitMq, null, null, null, null);

        connection.ShouldBeNull();
        _console.ErrorLines.ShouldContain(e => e.Contains("Configuration file not found"));
    }
}
