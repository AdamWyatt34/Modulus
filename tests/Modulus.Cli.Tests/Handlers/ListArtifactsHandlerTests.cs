using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class ListArtifactsHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private const string Slnx = @"C:\work\EShop\EShop.slnx";
    private const string SolutionRoot = @"C:\work\EShop";

    private ListArtifactsHandler CreateHandler()
        => new(_fs, _console, new SolutionFinder(_fs));

    private void SeedSolution()
    {
        _fs.SetCurrentDirectory(SolutionRoot);
        _fs.SeedFile(Slnx, "<Solution></Solution>");

        // Orders module: one event, one consumer, two entities.
        var orders = Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src");
        _fs.SeedFile(Path.Combine(orders, "Orders.Integration", "IntegrationEvents", "OrderPlacedEvent.cs"), "// event");
        _fs.SeedFile(Path.Combine(orders, "Orders.Infrastructure", "IntegrationEventHandlers", "InvoiceRequestedHandler.cs"), "// handler");
        _fs.SeedFile(Path.Combine(orders, "Orders.Domain", "Entities", "Order.cs"), "// entity");
        _fs.SeedFile(Path.Combine(orders, "Orders.Domain", "Entities", "OrderLine.cs"), "// entity");

        // Shipping module: consumer only; a non-Handler file that must not be listed.
        var shipping = Path.Combine(SolutionRoot, "src", "Modules", "Shipping", "src");
        _fs.SeedFile(Path.Combine(shipping, "Shipping.Infrastructure", "IntegrationEventHandlers", "OrderPlacedHandler.cs"), "// handler");
        _fs.SeedFile(Path.Combine(shipping, "Shipping.Infrastructure", "IntegrationEventHandlers", "Helpers.cs"), "// not a handler");
    }

    [Fact]
    public void ListEvents_finds_events_by_convention()
    {
        SeedSolution();

        var exit = CreateHandler().Execute(ArtifactConvention.Events, Slnx, json: false);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("Orders") && l.Contains("OrderPlacedEvent"));
        _console.Lines.ShouldNotContain(l => l.Contains("Shipping") && l.Contains("Handler"));
    }

    [Fact]
    public void ListConsumers_matches_only_handler_suffix()
    {
        SeedSolution();

        var exit = CreateHandler().Execute(ArtifactConvention.Consumers, Slnx, json: false);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("Orders") && l.Contains("InvoiceRequestedHandler"));
        _console.Lines.ShouldContain(l => l.Contains("Shipping") && l.Contains("OrderPlacedHandler"));
        _console.Lines.ShouldNotContain(l => l.Contains("Helpers"));
    }

    [Fact]
    public void ListEntities_lists_all_domain_entities()
    {
        SeedSolution();

        var exit = CreateHandler().Execute(ArtifactConvention.Entities, Slnx, json: false);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("Order") && !l.Contains("OrderLine"));
        _console.Lines.ShouldContain(l => l.Contains("OrderLine"));
    }

    [Fact]
    public void Json_output_is_a_parseable_array()
    {
        SeedSolution();

        var exit = CreateHandler().Execute(ArtifactConvention.Consumers, Slnx, json: true);

        exit.ShouldBe(0);
        var payload = string.Join("\n", _console.Lines);
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        doc.RootElement.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
        doc.RootElement.GetArrayLength().ShouldBe(2);
        doc.RootElement[0].GetProperty("module").GetString().ShouldNotBeNullOrEmpty();
        doc.RootElement[0].GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        doc.RootElement[0].GetProperty("path").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void No_artifacts_reports_and_returns_zero()
    {
        _fs.SetCurrentDirectory(SolutionRoot);
        _fs.SeedFile(Slnx, "<Solution></Solution>");

        var exit = CreateHandler().Execute(ArtifactConvention.Events, Slnx, json: false);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("No integration events found"));
    }

    [Fact]
    public void No_solution_fails()
    {
        _fs.SetCurrentDirectory(@"C:\elsewhere");

        var exit = CreateHandler().Execute(ArtifactConvention.Events, solutionPath: null, json: false);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(e => e.Contains("Could not find a solution file"));
    }
}
