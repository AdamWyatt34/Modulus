using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class AddModuleHandlerTests
{
    private const string HostCsprojPath = @"C:\work\EShop\src\EShop.WebApi\EShop.WebApi.csproj";

    private readonly FakeFileSystem _fs = new();
    private readonly FakeProcessRunner _proc = new();
    private readonly FakeConsole _console = new();

    private AddModuleHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new AddModuleHandler(_fs, _proc, _console, solutionFinder);
    }

    private void SeedModulusSolution()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAllModules(builder.Configuration);
            var app = builder.Build();
            app.MapAllModuleEndpoints();
            app.Run();
            """);
        _fs.SeedFile(HostCsprojPath,
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n" +
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\BuildingBlocks.Application\\BuildingBlocks.Application.csproj\" />\n" +
            "    <ProjectReference Include=\"..\\BuildingBlocks.Infrastructure\\BuildingBlocks.Infrastructure.csproj\" />\n" +
            "    <ProjectReference Include=\"..\\BuildingBlocks.Integration\\BuildingBlocks.Integration.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
    }

    [Fact]
    public async Task AddModule_creates_expected_project_structure()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        result.ShouldBe(0);

        // Source projects
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Catalog.Domain.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Catalog.Application.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Catalog.Infrastructure.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Integration\Catalog.Integration.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Catalog.Api.csproj").ShouldBeTrue();

        // Test projects
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Unit\Catalog.Tests.Unit.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Integration\Catalog.Tests.Integration.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Architecture\Catalog.Tests.Architecture.csproj").ShouldBeTrue();

        // Key source files
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\CatalogModule.cs").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Persistence\CatalogDbContext.cs").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\CatalogEndpointRegistration.cs").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetSample.cs").ShouldBeTrue();

        // Sample query files
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Samples\GetSampleQuery.cs").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Samples\GetSampleQueryHandler.cs").ShouldBeTrue();

        // Integration test files
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Integration\CatalogIntegrationTestBase.cs").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Integration\CatalogEndpointTests.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddModule_module_class_implements_IModuleRegistration()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\CatalogModule.cs");
        content.ShouldContain("IModuleRegistration");
        content.ShouldContain("ConfigureServices");
        content.ShouldContain("ConfigureEndpoints");
        content.ShouldContain("MapCatalogEndpoints");
    }

    [Fact]
    public async Task AddModule_rejects_invalid_csharp_identifier()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("123Bad", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task AddModule_rejects_duplicate_module()
    {
        SeedModulusSolution();
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("already exists"));
    }

    [Fact]
    public async Task AddModule_with_no_endpoints_skips_api_project()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: true);

        // No Api project
        _fs.AllFiles.Keys.ShouldNotContain(k =>
            k.Contains("Catalog.Api", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddModule_with_no_endpoints_removes_api_from_arch_tests()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: true);

        var archTestPath = _fs.AllFiles.Keys.FirstOrDefault(k =>
            k.Contains("LayerDependencyTests.cs", StringComparison.OrdinalIgnoreCase));
        archTestPath.ShouldNotBeNull();

        var content = _fs.ReadAllText(archTestPath);
        content.ShouldNotContain("ApiAssembly");
        content.ShouldNotContain("Domain_should_not_depend_on_Api");
        content.ShouldNotContain("Application_should_not_depend_on_Api");
        content.ShouldNotContain("Infrastructure_should_not_depend_on_Api");
    }

    [Fact]
    public async Task AddModule_with_no_endpoints_strips_api_from_module_class()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: true);

        var modulePath = _fs.AllFiles.Keys.FirstOrDefault(k =>
            k.Contains("CatalogModule.cs", StringComparison.OrdinalIgnoreCase));
        modulePath.ShouldNotBeNull();

        var content = _fs.ReadAllText(modulePath);
        content.ShouldNotContain(".Api.Endpoints");
        content.ShouldNotContain("MapCatalogEndpoints");
        content.ShouldContain("ConfigureEndpoints");
        content.ShouldContain("return endpoints;");
    }

    [Fact]
    public async Task AddModule_calls_dotnet_sln_add_for_each_project()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        var slnAddCalls = _proc.Invocations
            .Where(i => i.Command == "dotnet" && i.Arguments.Contains("sln") && i.Arguments.Contains("add"))
            .ToList();

        // Should have calls for each csproj (5 src + 3 test = 8)
        slnAddCalls.Count.ShouldBe(8);
    }

    [Fact]
    public async Task AddModule_module_name_containing_tests_substring_classifies_projects_correctly()
    {
        // A module named "Contests" contains "tests" as a substring (Con-TESTS). Classifying by
        // Contains("tests") over the *full absolute path* (which includes "...\Modules\Contests\...")
        // misfiles every src project into the /tests/ solution folder. Classification must come
        // from the template-relative output path's own "src"/"tests" first segment instead, which
        // has nothing to do with the module's name.
        SeedModulusSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Contests", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        result.ShouldBe(0);

        // PathGuard.EnsureContained returns a path using the running OS's own separator
        // (Path.DirectorySeparatorChar), so the argument text itself is '/'-separated on Linux
        // and '\'-separated on Windows — compare separator-agnostically via PathText.Equals
        // rather than an exact-text Contains against one hardcoded literal form.
        var domainCsproj = @"C:\work\EShop\src\Modules\Contests\src\Contests.Domain\Contests.Domain.csproj";
        var domainSlnAdd = _proc.Invocations.Single(i => i.Arguments.Any(a => PathText.Equals(a, domainCsproj)));
        domainSlnAdd.Arguments.ShouldContain("/src/Modules/Contests/");

        var unitTestCsproj = @"C:\work\EShop\src\Modules\Contests\tests\Contests.Tests.Unit\Contests.Tests.Unit.csproj";
        var unitTestSlnAdd = _proc.Invocations.Single(i => i.Arguments.Any(a => PathText.Equals(a, unitTestCsproj)));
        unitTestSlnAdd.Arguments.ShouldContain("/tests/Modules/Contests/");
    }

    [Fact]
    public async Task AddModule_solution_checked_out_under_a_tests_named_directory_classifies_projects_correctly()
    {
        // The other scenario item 12 describes: the *solution root itself* sits under a
        // directory whose name contains "tests" — a raw Contains("tests") on the full absolute
        // path would misfile every src project across the whole solution, not just this module.
        _fs.SetCurrentDirectory(@"C:\work\integration-tests-repo\EShop");
        _fs.SeedFile(@"C:\work\integration-tests-repo\EShop\EShop.slnx", "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\integration-tests-repo\EShop\src\EShop.WebApi\Program.cs", "// program");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync(
            "Catalog", @"C:\work\integration-tests-repo\EShop\EShop.slnx", noEndpoints: false);

        result.ShouldBe(0);
        var domainCsproj = @"C:\work\integration-tests-repo\EShop\src\Modules\Catalog\src\Catalog.Domain\Catalog.Domain.csproj";
        var domainSlnAdd = _proc.Invocations.Single(i => i.Arguments.Any(a => PathText.Equals(a, domainCsproj)));
        domainSlnAdd.Arguments.ShouldContain("/src/Modules/Catalog/");
    }

    [Fact]
    public async Task AddModule_runs_dotnet_restore()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        _proc.Invocations.ShouldContain(i => i.Command == "dotnet" && i.Arguments.Count == 1 && i.Arguments[0] == "restore");
    }

    [Fact]
    public async Task AddModule_returns_error_when_solution_not_found()
    {
        _fs.SetCurrentDirectory(@"C:\empty");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", null, noEndpoints: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Could not find"));
    }

    [Fact]
    public async Task AddModule_returns_error_when_not_modulus_solution()
    {
        _fs.SetCurrentDirectory(@"C:\work\Other");
        _fs.SeedFile(@"C:\work\Other\Other.slnx", "<Solution />");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", @"C:\work\Other\Other.slnx", noEndpoints: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("does not appear to be a Modulus solution"));
    }

    [Fact]
    public async Task AddModule_EndpointRegistration_documents_authorization_opt_in()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\CatalogEndpointRegistration.cs");

        // The scaffold ships anonymous because Program.cs has no registered auth scheme;
        // calling .RequireAuthorization() without one would throw at first request. The
        // template documents the exact line to uncomment after wiring auth.
        content.ShouldContain("// SECURITY:");
        content.ShouldContain("RequireAuthorization()");
        content.ShouldContain("var group = app.MapGroup(\"/api/catalog\");");
    }

    [Fact]
    public async Task AddModule_GetSample_uses_sample_route_not_root()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Api\Endpoints\GetSample.cs");
        content.ShouldContain("app.MapGet(\"/sample\"");
        content.ShouldNotContain("app.MapGet(\"/\",");
    }

    [Fact]
    public async Task AddModule_scaffolded_integration_test_targets_the_sample_route()
    {
        // H-CLI4: the module maps only /api/{module}/sample — a test hitting /api/{module}
        // itself 404s on a fresh `dotnet test`.
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Integration\CatalogEndpointTests.cs");
        content.ShouldContain("\"/api/catalog/sample\"");
        content.ShouldNotContain("\"/api/catalog\"");
    }

    [Fact]
    public async Task AddModule_with_no_endpoints_excludes_endpoint_test_file()
    {
        // H-CLI4: with --no-endpoints the module maps zero HTTP endpoints, so a test asserting
        // one returns 200 would be permanently red (or meaningless) out of the box.
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: true);

        _fs.AllFiles.Keys.ShouldNotContain(k => k.EndsWith("CatalogEndpointTests.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddModule_with_no_endpoints_still_includes_integration_test_base()
    {
        // Only the endpoint-specific test is excluded — the WebApplicationFactory harness base
        // class remains available for any integration tests the user adds later.
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: true);

        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\tests\Catalog.Tests.Integration\CatalogIntegrationTestBase.cs").ShouldBeTrue();
    }

    [Fact]
    public async Task AddModule_imports_IUnitOfWork_from_library_namespace()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        var content = _fs.ReadAllText(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\CatalogModule.cs");
        content.ShouldContain("using Modulus.Mediator.Abstractions;");
        content.ShouldNotContain("using EShop.BuildingBlocks.Application;");
    }

    // ── C2: host wiring — module discovery is compile-time over the host's referenced ────────
    // ── assemblies, so the .slnx entry alone never makes the host load the module.       ────

    [Fact]
    public async Task AddModule_adds_host_project_reference_to_module_infrastructure()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        result.ShouldBe(0);
        var hostCsproj = _fs.ReadAllText(HostCsprojPath);
        hostCsproj.ShouldContain("..\\Modules\\Catalog\\src\\Catalog.Infrastructure\\Catalog.Infrastructure.csproj");
    }

    [Fact]
    public async Task AddModule_prints_host_wiring_in_summary_output()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        _console.Lines.ShouldContain(l =>
            l.Contains("Host wiring") && l.Contains("EShop.WebApi") && l.Contains("Catalog.Infrastructure"));
    }

    [Fact]
    public async Task AddModule_still_wires_host_reference_when_no_endpoints()
    {
        // The module's Infrastructure project (ConfigureServices, DI) exists regardless of
        // --no-endpoints — only the Api project is skipped — so the host still needs the
        // reference for the module's non-HTTP registrations to run.
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: true);

        var hostCsproj = _fs.ReadAllText(HostCsprojPath);
        hostCsproj.ShouldContain("Catalog.Infrastructure.csproj");
    }

    [Fact]
    public async Task AddModule_host_reference_is_idempotent_when_already_wired()
    {
        SeedModulusSolution();
        // Simulate a prior partially-completed run (or manual pre-wiring): the host already
        // references the module's Infrastructure project before add-module runs.
        var preWired = _fs.ReadAllText(HostCsprojPath).Replace(
            "</Project>",
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\Modules\\Catalog\\src\\Catalog.Infrastructure\\Catalog.Infrastructure.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        _fs.SeedFile(HostCsprojPath, preWired);
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        result.ShouldBe(0);
        var hostCsproj = _fs.ReadAllText(HostCsprojPath);
        CountOccurrences(hostCsproj, "Catalog.Infrastructure.csproj").ShouldBe(1);
        // Nothing new was added, so the summary must not claim it was.
        _console.Lines.ShouldNotContain(l => l.Contains("Host wiring"));
    }

    [Fact]
    public async Task AddModule_missing_host_csproj_warns_but_still_creates_module()
    {
        // A solution whose host project directory has Program.cs but no csproj (damaged, or a
        // shape this tool doesn't recognize) must not abort the whole scaffold — every module
        // file has already been written to disk by the point the host is wired.
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        result.ShouldBe(0);
        _fs.FileExists(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Infrastructure\Catalog.Infrastructure.csproj").ShouldBeTrue();
        _console.ErrorLines.ShouldContain(l => l.Contains("Warning") && l.Contains("host project file"));
    }

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
}
