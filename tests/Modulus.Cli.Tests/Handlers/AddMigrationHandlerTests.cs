using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class AddMigrationHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();
    private readonly FakeProcessRunner _processRunner = new();

    private const string SolutionRoot = @"C:\work\Shop";
    private const string SlnxPath = @"C:\work\Shop\Shop.slnx";

    private AddMigrationHandler CreateHandler()
        => new(_fs, _processRunner, _console, new SolutionFinder(_fs));

    private void SeedModulusSolution(string moduleName = "Orders")
    {
        _fs.SetCurrentDirectory(SolutionRoot);
        _fs.SeedFile(SlnxPath, "<Solution />");
        _fs.SeedFile(Path.Combine(SolutionRoot, "src", "Shop.WebApi", "Program.cs"), "// host");
        _fs.SeedFile(Path.Combine(SolutionRoot, "src", "Shop.WebApi", "Shop.WebApi.csproj"), "<Project />");
        _fs.SeedFile(
            Path.Combine(SolutionRoot, "src", "Modules", moduleName, "src", $"{moduleName}.Infrastructure", $"{moduleName}.Infrastructure.csproj"),
            "<Project />");
    }

    [Fact]
    public async Task Runs_dotnet_ef_with_inferred_project_and_startup_project()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("AddOrderTable", "Orders", SlnxPath);

        exit.ShouldBe(0);
        var invocation = _processRunner.Invocations.ShouldHaveSingleItem();
        invocation.Command.ShouldBe("dotnet");
        invocation.WorkingDirectory.ShouldBe(SolutionRoot);
        invocation.Arguments.ShouldBe([
            "ef", "migrations", "add", "AddOrderTable",
            "--project", Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src", "Orders.Infrastructure", "Orders.Infrastructure.csproj"),
            "--startup-project", Path.Combine(SolutionRoot, "src", "Shop.WebApi", "Shop.WebApi.csproj"),
            "--context", "OrdersDbContext",
            "--output-dir", "Migrations",
        ]);
        _console.SuccessLines.ShouldContain(l => l.Contains("AddOrderTable"));
    }

    [Fact]
    public async Task Custom_context_and_output_dir_flow_through()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync(
            "Initial", "Orders", SlnxPath, context: "OrdersWriteContext", outputDir: "Persistence/Migrations");

        exit.ShouldBe(0);
        var arguments = _processRunner.Invocations.ShouldHaveSingleItem().Arguments;
        arguments.ShouldContain("OrdersWriteContext");
        arguments.ShouldContain("Persistence/Migrations");
    }

    [Fact]
    public async Task Dry_run_prints_the_invocation_and_starts_nothing()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("AddOrderTable", "Orders", SlnxPath, dryRun: true);

        exit.ShouldBe(0);
        _processRunner.Invocations.ShouldBeEmpty();
        _console.Lines.ShouldContain(l => l.Contains("Dry run"));
        _console.Lines.ShouldContain(l => l.Contains("ef migrations add AddOrderTable"));
    }

    [Fact]
    public async Task Invalid_migration_name_is_rejected()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("add order table", "Orders", SlnxPath);

        exit.ShouldBe(1);
        _processRunner.Invocations.ShouldBeEmpty();
        _console.ErrorLines.ShouldContain(l => l.Contains("not a valid migration name"));
    }

    [Fact]
    public async Task Unknown_module_reports_error_before_running_anything()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("Initial", "Billing", SlnxPath);

        exit.ShouldBe(1);
        _processRunner.Invocations.ShouldBeEmpty();
        _console.ErrorLines.ShouldContain(l => l.Contains("Billing") && l.Contains("not found"));
    }

    [Fact]
    public async Task Module_without_infrastructure_project_reports_error()
    {
        SeedModulusSolution();
        _fs.SeedDirectory(Path.Combine(SolutionRoot, "src", "Modules", "Empty"));
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("Initial", "Empty", SlnxPath);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Infrastructure project not found"));
    }

    [Fact]
    public async Task Failed_dotnet_ef_reports_tool_install_hint()
    {
        SeedModulusSolution();
        _processRunner.ExitCodeToReturn = 1;
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("Initial", "Orders", SlnxPath);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("dotnet tool install --global dotnet-ef"));
    }
}
