using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class AddEventHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private AddEventHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new AddEventHandler(_fs, _console, solutionFinder);
    }

    private void SeedModulusSolutionWithModule()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Orders");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Orders\src\Orders.Integration");
    }

    private const string EventPath =
        @"C:\work\EShop\src\Modules\Orders\src\Orders.Integration\IntegrationEvents\OrderShipped.cs";

    // ── File creation ────────────────────────────────────────────

    [Fact]
    public async Task AddEvent_creates_event_record()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Orders", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(0);
        _fs.FileExists(EventPath).ShouldBeTrue();
    }

    [Fact]
    public async Task AddEvent_event_has_correct_namespace()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("OrderShipped", "Orders", @"C:\work\EShop\EShop.slnx", null);

        var content = _fs.ReadAllText(EventPath);
        content.ShouldContain("namespace EShop.Orders.Integration.IntegrationEvents;");
    }

    [Fact]
    public async Task AddEvent_implements_IntegrationEvent_with_using()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("OrderShipped", "Orders", @"C:\work\EShop\EShop.slnx", null);

        var content = _fs.ReadAllText(EventPath);
        content.ShouldContain("using Modulus.Messaging.Abstractions;");
        content.ShouldContain(": IntegrationEvent;");
    }

    [Fact]
    public async Task AddEvent_without_properties_is_parameterless_record()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("OrderShipped", "Orders", @"C:\work\EShop\EShop.slnx", null);

        var content = _fs.ReadAllText(EventPath);
        content.ShouldContain("public sealed record OrderShipped : IntegrationEvent;");
    }

    [Fact]
    public async Task AddEvent_with_properties_creates_positional_record()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("OrderShipped", "Orders", @"C:\work\EShop\EShop.slnx", "OrderId:Guid,Total:decimal");

        var content = _fs.ReadAllText(EventPath);
        content.ShouldContain("public sealed record OrderShipped(Guid OrderId, decimal Total) : IntegrationEvent;");
    }

    // ── Validation errors ────────────────────────────────────────

    [Fact]
    public async Task AddEvent_rejects_invalid_event_name()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("123Bad", "Orders", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddEvent_rejects_invalid_module_name()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "123Bad", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddEvent_rejects_invalid_properties()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Orders", @"C:\work\EShop\EShop.slnx", "BadFormat");

        result.ShouldBe(1);
        _console.ErrorLines.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task AddEvent_rejects_nonexistent_module()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Catalog", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    [Fact]
    public async Task AddEvent_rejects_missing_integration_project()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Orders"); // module exists, but no Integration project
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Orders", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Integration"));
    }

    [Fact]
    public async Task AddEvent_rejects_duplicate_event()
    {
        SeedModulusSolutionWithModule();
        _fs.SeedFile(EventPath, "existing");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Orders", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("already exists"));
    }

    [Fact]
    public async Task AddEvent_returns_error_when_solution_not_found()
    {
        _fs.SetCurrentDirectory(@"C:\empty");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Orders", null, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Could not find"));
    }

    [Fact]
    public async Task AddEvent_prints_success_message()
    {
        SeedModulusSolutionWithModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Orders", @"C:\work\EShop\EShop.slnx", null);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("OrderShipped") && l.Contains("Orders"));
    }
}
