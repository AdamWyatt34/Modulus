using System.Text.Json;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class DoctorHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private const string Slnx = @"C:\work\EShop\EShop.slnx";
    private const string SolutionRoot = @"C:\work\EShop";

    private DoctorHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new DoctorHandler(_fs, _console, solutionFinder);
    }

    private const string DirectoryPackagesProps =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageVersion Include=\"ModulusKit.Mediator\" Version=\"1.2.0\" />\n" +
        "    <PackageVersion Include=\"ModulusKit.Mediator.Abstractions\" Version=\"1.2.0\" />\n" +
        "    <PackageVersion Include=\"ModulusKit.Messaging\" Version=\"1.2.0\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    private const string SkewedDirectoryPackagesProps =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageVersion Include=\"ModulusKit.Mediator\" Version=\"1.2.0\" />\n" +
        "    <PackageVersion Include=\"ModulusKit.Messaging\" Version=\"1.3.0\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    private void SeedHealthySolution()
    {
        _fs.SetCurrentDirectory(SolutionRoot);
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        _fs.SeedFile(Path.Combine(SolutionRoot, "src", "EShop.WebApi", "Program.cs"), "// program");
        _fs.SeedFile(Path.Combine(SolutionRoot, "Directory.Packages.props"), DirectoryPackagesProps);

        // Module with all expected projects.
        var moduleSrc = Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src");
        SeedProject(Path.Combine(moduleSrc, "Orders.Domain", "Orders.Domain.csproj"));
        SeedProject(Path.Combine(moduleSrc, "Orders.Application", "Orders.Application.csproj"));
        SeedProject(Path.Combine(moduleSrc, "Orders.Infrastructure", "Orders.Infrastructure.csproj"));
        SeedProject(Path.Combine(moduleSrc, "Orders.Integration", "Orders.Integration.csproj"));
    }

    private void SeedProject(string csprojPath, string content = MinimalCsproj)
        => _fs.SeedFile(csprojPath, content);

    private const string MinimalCsproj = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n";

    // ── Overall pass/fail ─────────────────────────────────────────

    [Fact]
    public async Task Doctor_healthy_solution_returns_zero()
    {
        SeedHealthySolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
    }

    [Fact]
    public async Task Doctor_missing_slnx_returns_one()
    {
        _fs.SetCurrentDirectory(@"C:\empty");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(null, json: false, strict: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Could not find"));
    }

    // ── PackageVersions ───────────────────────────────────────────

    [Fact]
    public async Task Doctor_version_skew_warns_but_passes_without_strict()
    {
        SeedHealthySolution();
        _fs.SeedFile(Path.Combine(SolutionRoot, "Directory.Packages.props"), SkewedDirectoryPackagesProps);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("PackageVersions") && l.Contains("inconsistent"));
    }

    [Fact]
    public async Task Doctor_version_skew_returns_two_with_strict()
    {
        SeedHealthySolution();
        _fs.SeedFile(Path.Combine(SolutionRoot, "Directory.Packages.props"), SkewedDirectoryPackagesProps);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: true);

        result.ShouldBe(2);
    }

    [Fact]
    public async Task Doctor_missing_directory_packages_props_fails()
    {
        SeedHealthySolution();
        // Overwrite the healthy fixture by seeding a fresh solution without Directory.Packages.props.
        var fs = new FakeFileSystem();
        fs.SetCurrentDirectory(SolutionRoot);
        fs.SeedFile(Slnx, "<Solution></Solution>");
        fs.SeedFile(Path.Combine(SolutionRoot, "src", "EShop.WebApi", "Program.cs"), "// program");
        var solutionFinder = new SolutionFinder(fs);
        var console = new FakeConsole();
        var handler = new DoctorHandler(fs, console, solutionFinder);

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(1);
        console.ErrorLines.ShouldContain(l => l.Contains("Directory.Packages.props"));
    }

    // ── ModuleArtifacts ───────────────────────────────────────────

    [Fact]
    public async Task Doctor_module_missing_project_warns()
    {
        SeedHealthySolution();
        // Remove the Integration project for a second module, leaving the rest.
        var moduleSrc = Path.Combine(SolutionRoot, "src", "Modules", "Billing", "src");
        SeedProject(Path.Combine(moduleSrc, "Billing.Domain", "Billing.Domain.csproj"));
        SeedProject(Path.Combine(moduleSrc, "Billing.Application", "Billing.Application.csproj"));
        SeedProject(Path.Combine(moduleSrc, "Billing.Infrastructure", "Billing.Infrastructure.csproj"));
        // Billing.Integration deliberately omitted.
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("Billing") && l.Contains("Billing.Integration"));
    }

    // ── ProjectReferences ─────────────────────────────────────────

    [Fact]
    public async Task Doctor_broken_project_reference_fails()
    {
        SeedHealthySolution();
        var csprojPath = Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src", "Orders.Application", "Orders.Application.csproj");
        var content =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\Orders.NoSuchProject\\Orders.NoSuchProject.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n";
        _fs.SeedFile(csprojPath, content);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("ProjectReferences") && l.Contains("Orders.NoSuchProject"));
    }

    [Fact]
    public async Task Doctor_valid_project_reference_passes()
    {
        SeedHealthySolution();
        var appCsproj = Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src", "Orders.Application", "Orders.Application.csproj");
        var content =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\Orders.Domain\\Orders.Domain.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n";
        _fs.SeedFile(appCsproj, content);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
    }

    // ── MessagingConfig ───────────────────────────────────────────

    [Fact]
    public async Task Doctor_messaging_referenced_without_config_warns()
    {
        SeedHealthySolution();
        var csprojPath = Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src", "Orders.Infrastructure", "Orders.Infrastructure.csproj");
        var content =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"ModulusKit.Messaging\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n";
        _fs.SeedFile(csprojPath, content);
        // No appsettings.json under the WebApi project.
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("MessagingConfig") && l.Contains("Messaging"));
    }

    [Fact]
    public async Task Doctor_messaging_config_with_valid_transport_passes()
    {
        SeedHealthySolution();
        var csprojPath = Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src", "Orders.Infrastructure", "Orders.Infrastructure.csproj");
        _fs.SeedFile(csprojPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"ModulusKit.Messaging\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
        var appsettingsPath = Path.Combine(SolutionRoot, "src", "EShop.WebApi", "appsettings.json");
        _fs.SeedFile(appsettingsPath, "{ \"Messaging\": { \"Transport\": \"InMemory\" } }");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("MessagingConfig"));
    }

    [Fact]
    public async Task Doctor_messaging_rabbitmq_without_connection_warns()
    {
        SeedHealthySolution();
        var csprojPath = Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src", "Orders.Infrastructure", "Orders.Infrastructure.csproj");
        _fs.SeedFile(csprojPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"ModulusKit.Messaging\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
        var appsettingsPath = Path.Combine(SolutionRoot, "src", "EShop.WebApi", "appsettings.json");
        _fs.SeedFile(appsettingsPath, "{ \"Messaging\": { \"Transport\": \"RabbitMq\" } }");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("MessagingConfig") && l.Contains("ConnectionString"));
    }

    // ── MigrationGuidance ─────────────────────────────────────────

    [Fact]
    public async Task Doctor_outbox_without_migration_call_warns()
    {
        SeedHealthySolution();
        _fs.SeedFile(
            Path.Combine(SolutionRoot, "src", "EShop.WebApi", "Program.cs"),
            "builder.Services.AddModulusOutbox(o => o.UseSqlServer(\"...\"));\napp.Run();\n");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("MigrationGuidance") && l.Contains("UseModulusMessagingMigrationsAsync"));
    }

    [Fact]
    public async Task Doctor_outbox_with_migration_call_passes()
    {
        SeedHealthySolution();
        _fs.SeedFile(
            Path.Combine(SolutionRoot, "src", "EShop.WebApi", "Program.cs"),
            "builder.Services.AddModulusOutbox(o => o.UseSqlServer(\"...\"));\n" +
            "await app.UseModulusMessagingMigrationsAsync();\napp.Run();\n");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("MigrationGuidance"));
    }

    // ── --json output ─────────────────────────────────────────────

    [Fact]
    public async Task Doctor_json_output_parses_with_expected_statuses()
    {
        SeedHealthySolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: true, strict: false);

        result.ShouldBe(0);
        _console.Lines.Count.ShouldBe(1);
        using var document = JsonDocument.Parse(_console.Lines[0]);
        var checks = document.RootElement.GetProperty("checks");
        checks.GetArrayLength().ShouldBeGreaterThan(0);
        foreach (var check in checks.EnumerateArray())
        {
            check.GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
            check.GetProperty("status").GetString().ShouldBeOneOf("Pass", "Warn", "Fail");
            check.GetProperty("message").GetString().ShouldNotBeNullOrEmpty();
        }

        var summary = document.RootElement.GetProperty("summary");
        summary.GetProperty("fail").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Doctor_json_output_reflects_failures()
    {
        _fs.SetCurrentDirectory(SolutionRoot);
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        _fs.SeedFile(Path.Combine(SolutionRoot, "src", "EShop.WebApi", "Program.cs"), "// program");
        // No Directory.Packages.props -> Fail.
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: true, strict: false);

        result.ShouldBe(1);
        using var document = JsonDocument.Parse(_console.Lines[0]);
        document.RootElement.GetProperty("summary").GetProperty("fail").GetInt32().ShouldBeGreaterThan(0);
    }

    // ── Human output summary ──────────────────────────────────────

    [Fact]
    public async Task Doctor_human_output_prints_summary_line()
    {
        SeedHealthySolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync(Slnx, json: false, strict: false);

        _console.Lines.ShouldContain(l => l.StartsWith("Summary:"));
    }
}
