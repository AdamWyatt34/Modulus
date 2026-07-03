using System.Xml.Linq;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class AddConsumerHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private const string Slnx = @"C:\work\EShop\EShop.slnx";

    private const string HandlerPath =
        @"C:\work\EShop\src\Modules\Shipping\src\Shipping.Infrastructure\IntegrationEventHandlers\OrderShippedHandler.cs";

    private const string ShippingInfraCsproj =
        @"C:\work\EShop\src\Modules\Shipping\src\Shipping.Infrastructure\Shipping.Infrastructure.csproj";

    private const string InfraCsprojContent =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <ItemGroup>\n" +
        "    <ProjectReference Include=\"..\\Shipping.Application\\Shipping.Application.csproj\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    private const string OrderShippedEvent =
        "using Modulus.Messaging.Abstractions;\n\n" +
        "namespace EShop.Orders.Integration.IntegrationEvents;\n\n" +
        "public sealed record OrderShipped : IntegrationEvent;\n";

    private AddConsumerHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new AddConsumerHandler(_fs, _console, solutionFinder);
    }

    /// <summary>
    /// Seeds a solution with a consuming module (Shipping, with an Infrastructure project) and a
    /// source module (Orders) that owns the OrderShipped event.
    /// </summary>
    private void SeedSolution()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");

        // Consuming module
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Shipping\src\Shipping.Infrastructure");
        _fs.SeedFile(ShippingInfraCsproj, InfraCsprojContent);

        // Source module owning the event — both the event file and its Integration project.
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Orders\src\Orders.Integration\IntegrationEvents\OrderShipped.cs",
            OrderShippedEvent);
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Orders\src\Orders.Integration\Orders.Integration.csproj",
            MinimalCsproj);
    }

    private const string MinimalCsproj = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n";

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    // ── File creation + content ──────────────────────────────────

    [Fact]
    public async Task AddConsumer_creates_handler_file()
    {
        SeedSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(0);
        _fs.FileExists(HandlerPath).ShouldBeTrue();
    }

    [Fact]
    public async Task AddConsumer_handler_has_correct_namespace()
    {
        SeedSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        var content = _fs.ReadAllText(HandlerPath);
        content.ShouldContain("namespace EShop.Shipping.Infrastructure.IntegrationEventHandlers;");
    }

    [Fact]
    public async Task AddConsumer_handler_implements_interface()
    {
        SeedSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        var content = _fs.ReadAllText(HandlerPath);
        content.ShouldContain("public sealed class OrderShippedHandler : IIntegrationEventHandler<OrderShipped>");
        content.ShouldContain("Task Handle(OrderShipped @event, CancellationToken cancellationToken = default)");
    }

    [Fact]
    public async Task AddConsumer_handler_uses_event_namespace()
    {
        SeedSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        var content = _fs.ReadAllText(HandlerPath);
        content.ShouldContain("using EShop.Orders.Integration.IntegrationEvents;");
        content.ShouldContain("using Modulus.Messaging.Abstractions;");
    }

    // ── Cross-module project reference wiring ────────────────────

    [Fact]
    public async Task AddConsumer_adds_cross_module_project_reference()
    {
        SeedSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        var csproj = _fs.ReadAllText(ShippingInfraCsproj);
        csproj.ShouldContain("..\\..\\..\\Orders\\src\\Orders.Integration\\Orders.Integration.csproj");
    }

    [Fact]
    public async Task AddConsumer_project_reference_is_idempotent()
    {
        SeedSolution();
        // Pre-seed the csproj with the reference already present.
        var withReference = InfraCsprojContent.Replace(
            "</Project>",
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\..\\..\\Orders\\src\\Orders.Integration\\Orders.Integration.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        _fs.SeedFile(ShippingInfraCsproj, withReference);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(0);
        var csproj = _fs.ReadAllText(ShippingInfraCsproj);
        CountOccurrences(csproj, "Orders.Integration.csproj").ShouldBe(1);
    }

    [Fact]
    public async Task AddConsumer_intra_module_event_wires_own_integration_reference()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Shipping\src\Shipping.Infrastructure");
        _fs.SeedFile(ShippingInfraCsproj, InfraCsprojContent);
        // Event owned by the same module that consumes it.
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Shipping\src\Shipping.Integration\IntegrationEvents\OrderShipped.cs",
            "using Modulus.Messaging.Abstractions;\n\nnamespace EShop.Shipping.Integration.IntegrationEvents;\n\npublic sealed record OrderShipped : IntegrationEvent;\n");
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Shipping\src\Shipping.Integration\Shipping.Integration.csproj",
            MinimalCsproj);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(0);
        var csproj = _fs.ReadAllText(ShippingInfraCsproj);
        csproj.ShouldContain("..\\..\\..\\Shipping\\src\\Shipping.Integration\\Shipping.Integration.csproj");
    }

    // ── Event location ───────────────────────────────────────────

    [Fact]
    public async Task AddConsumer_errors_when_event_not_found()
    {
        SeedSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("PaymentReceived", "Shipping", Slnx, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    [Fact]
    public async Task AddConsumer_errors_when_event_ambiguous()
    {
        SeedSolution();
        // Second module also declaring OrderShipped.
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Billing\src\Billing.Integration\IntegrationEvents\OrderShipped.cs",
            "namespace EShop.Billing.Integration.IntegrationEvents;\n\npublic sealed record OrderShipped : IntegrationEvent;\n");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("multiple modules"));
    }

    [Fact]
    public async Task AddConsumer_event_module_disambiguates()
    {
        SeedSolution();
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Billing\src\Billing.Integration\IntegrationEvents\OrderShipped.cs",
            "namespace EShop.Billing.Integration.IntegrationEvents;\n\npublic sealed record OrderShipped : IntegrationEvent;\n");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, "Orders");

        result.ShouldBe(0);
        var content = _fs.ReadAllText(HandlerPath);
        content.ShouldContain("using EShop.Orders.Integration.IntegrationEvents;");
    }

    // ── Validation + preconditions ───────────────────────────────

    [Fact]
    public async Task AddConsumer_rejects_invalid_event_name()
    {
        SeedSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("123Bad", "Shipping", Slnx, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddConsumer_rejects_nonexistent_consuming_module()
    {
        SeedSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Warehouse", Slnx, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("not found"));
    }

    [Fact]
    public async Task AddConsumer_rejects_missing_infrastructure_project()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Shipping"); // module but no Infrastructure project
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Orders\src\Orders.Integration\IntegrationEvents\OrderShipped.cs",
            OrderShippedEvent);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Infrastructure"));
    }

    [Fact]
    public async Task AddConsumer_rejects_duplicate_when_handler_and_reference_present()
    {
        SeedSolution();
        // A genuine duplicate: the handler exists AND the reference is already wired.
        var withReference = InfraCsprojContent.Replace(
            "</Project>",
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\..\\..\\Orders\\src\\Orders.Integration\\Orders.Integration.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        _fs.SeedFile(ShippingInfraCsproj, withReference);
        _fs.SeedFile(HandlerPath, "existing");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("already exists"));
    }

    [Fact]
    public async Task AddConsumer_repairs_reference_when_handler_exists_without_reference()
    {
        SeedSolution(); // csproj has no Orders.Integration reference yet
        _fs.SeedFile(HandlerPath, "// previously generated handler");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        // Re-running self-heals the partially-generated state instead of dead-ending.
        result.ShouldBe(0);
        _fs.ReadAllText(ShippingInfraCsproj)
            .ShouldContain("..\\..\\..\\Orders\\src\\Orders.Integration\\Orders.Integration.csproj");
        // The existing handler file is left untouched (no clobbering user edits).
        _fs.ReadAllText(HandlerPath).ShouldBe("// previously generated handler");
        _console.SuccessLines.ShouldContain(l => l.Contains("repaired"));
    }

    [Fact]
    public async Task AddConsumer_errors_when_infrastructure_csproj_missing()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");
        // Infrastructure directory exists, but the project file does not.
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Shipping\src\Shipping.Infrastructure");
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Orders\src\Orders.Integration\IntegrationEvents\OrderShipped.cs",
            OrderShippedEvent);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("project file was not found"));
        _fs.FileExists(HandlerPath).ShouldBeFalse(); // nothing generated before the failure
    }

    [Fact]
    public async Task AddConsumer_errors_when_csproj_malformed()
    {
        SeedSolution();
        // Unclosed root element — not well-formed XML, so the reference cannot be wired.
        _fs.SeedFile(ShippingInfraCsproj, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup />\n");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("well-formed"));
        _fs.FileExists(HandlerPath).ShouldBeFalse(); // nothing generated before the failure
    }

    [Fact]
    public async Task AddConsumer_errors_when_source_integration_csproj_missing()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");
        // Valid consuming Infrastructure project.
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Shipping\src\Shipping.Infrastructure");
        _fs.SeedFile(ShippingInfraCsproj, InfraCsprojContent);
        // The event source file exists, but the Orders.Integration *project file* does NOT —
        // a partially generated / damaged module. Wiring a reference to it would break the build.
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Orders\src\Orders.Integration\IntegrationEvents\OrderShipped.cs",
            OrderShippedEvent);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Orders.Integration") && l.Contains("not found"));
        _fs.FileExists(HandlerPath).ShouldBeFalse(); // nothing generated before the failure
        // The consuming csproj must not have been mutated.
        _fs.ReadAllText(ShippingInfraCsproj).ShouldBe(InfraCsprojContent);
    }

    [Fact]
    public async Task AddConsumer_ignores_commented_out_reference_and_wires_a_real_one()
    {
        SeedSolution();
        // A commented-out reference whose text mentions the target must NOT count as present.
        var withCommentedReference = InfraCsprojContent.Replace(
            "</Project>",
            "  <!-- <ProjectReference Include=\"..\\..\\..\\Orders\\src\\Orders.Integration\\Orders.Integration.csproj\" /> -->\n" +
            "</Project>");
        _fs.SeedFile(ShippingInfraCsproj, withCommentedReference);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(0);
        // A real ProjectReference element (not just the comment) must now exist.
        var includes = XDocument.Parse(_fs.ReadAllText(ShippingInfraCsproj))
            .Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .ToList();
        includes.ShouldContain(i => i != null && i.EndsWith("Orders.Integration.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddConsumer_returns_error_when_solution_not_found()
    {
        _fs.SetCurrentDirectory(@"C:\empty");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", null, null);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Could not find"));
    }

    [Fact]
    public async Task AddConsumer_prints_success_message()
    {
        SeedSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("OrderShipped", "Shipping", Slnx, null);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("OrderShippedHandler") && l.Contains("Shipping"));
    }
}
