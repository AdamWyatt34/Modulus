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

    [Fact]
    public async Task Doctor_msbuild_variable_project_reference_is_not_false_flagged()
    {
        // "$(SolutionDir)..." can't be resolved by static text inspection — only MSBuild knows
        // the property's value — so this must not be reported as a missing project.
        SeedHealthySolution();
        var appCsproj = Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src", "Orders.Application", "Orders.Application.csproj");
        var content =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"$(SolutionDir)Shared\\Shared.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n";
        _fs.SeedFile(appCsproj, content);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.ErrorLines.ShouldNotContain(l => l.Contains("ProjectReferences"));
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

    [Fact]
    public async Task Doctor_messaging_rabbitmq_aspire_connection_strings_shape_does_not_warn()
    {
        // Under Aspire, the RabbitMQ resource injects ConnectionStrings:messaging at run time via
        // service discovery — appsettings.json legitimately has no static ConnectionString.
        SeedHealthySolution();
        var csprojPath = Path.Combine(SolutionRoot, "src", "Modules", "Orders", "src", "Orders.Infrastructure", "Orders.Infrastructure.csproj");
        _fs.SeedFile(csprojPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"ModulusKit.Messaging\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
        var appsettingsPath = Path.Combine(SolutionRoot, "src", "EShop.WebApi", "appsettings.json");
        _fs.SeedFile(appsettingsPath,
            "{ \"Messaging\": { \"Transport\": \"RabbitMq\" }, \"ConnectionStrings\": { \"messaging\": \"\" } }");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("MessagingConfig") && l.Contains("Aspire"));
        _console.Lines.ShouldNotContain(l => l.Contains("MessagingConfig") && l.Contains("neither ConnectionString"));
    }

    [Fact]
    public async Task Doctor_messaging_rabbitmq_apphost_project_shape_does_not_warn()
    {
        // The second Aspire signal: an AppHost project anywhere in the solution, even if
        // appsettings.json itself carries no ConnectionStrings section at all.
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
        _fs.SeedFile(
            Path.Combine(SolutionRoot, "aspire", "EShop.AppHost", "EShop.AppHost.csproj"),
            MinimalCsproj);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("MessagingConfig") && l.Contains("Aspire"));
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

    [Fact]
    public async Task Doctor_pristine_scaffold_with_commented_out_guidance_does_not_warn()
    {
        // Mirrors Program.cs.template: the outbox/inbox registration and the migration call are
        // both commented-out guidance. A naive Contains-based match would find "AddModulusOutbox"
        // and "UseModulusMessagingMigrationsAsync" inside the comments and (by coincidence) still
        // pass — this asserts the intended, comment-aware behavior explicitly.
        SeedHealthySolution();
        _fs.SeedFile(
            Path.Combine(SolutionRoot, "src", "EShop.WebApi", "Program.cs"),
            "var builder = WebApplication.CreateBuilder(args);\n" +
            "//   builder.Services.AddModulusOutbox(o => o.UseSqlServer(\"...\"));\n" +
            "var app = builder.Build();\n" +
            "//   await app.UseModulusMessagingMigrationsAsync();\n" +
            "app.Run();\n");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.SuccessLines.ShouldContain(l => l.Contains("MigrationGuidance"));
    }

    [Fact]
    public async Task Doctor_real_outbox_registration_with_commented_out_migration_call_warns()
    {
        // The actual false-negative this fix targets: outbox registration was uncommented (real),
        // but the migration call is still commented out — a comment mentioning
        // UseModulusMessagingMigrationsAsync must not count as "calls it".
        SeedHealthySolution();
        _fs.SeedFile(
            Path.Combine(SolutionRoot, "src", "EShop.WebApi", "Program.cs"),
            "builder.Services.AddModulusOutbox(o => o.UseSqlServer(\"...\"));\n" +
            "//   await app.UseModulusMessagingMigrationsAsync();\n" +
            "app.Run();\n");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(Slnx, json: false, strict: false);

        result.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("MigrationGuidance") && l.Contains("UseModulusMessagingMigrationsAsync"));
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
