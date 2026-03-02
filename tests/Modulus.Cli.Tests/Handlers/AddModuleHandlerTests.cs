using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class AddModuleHandlerTests
{
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
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\ModuleRegistration.cs", """
            namespace EShop.WebApi;

            public static class ModuleRegistration
            {
                public static IServiceCollection AddModules(
                    this IServiceCollection services,
                    IConfiguration configuration)
                {
                    // Register modules here:

                    return services;
                }

                public static WebApplication MapModuleEndpoints(this WebApplication app)
                {
                    // Map module endpoints here:

                    return app;
                }
            }
            """);
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
    public async Task AddModule_updates_composition_root()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        var registration = _fs.ReadAllText(@"C:\work\EShop\src\EShop.WebApi\ModuleRegistration.cs");
        registration.ShouldContain("services.AddCatalogModule(configuration);");
        registration.ShouldContain("app.MapCatalogEndpoints();");
        registration.ShouldContain("using EShop.Catalog.Infrastructure;");
        registration.ShouldContain("using EShop.Catalog.Api.Endpoints;");
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

        // Registration should have AddModule but not MapEndpoints
        var registration = _fs.ReadAllText(@"C:\work\EShop\src\EShop.WebApi\ModuleRegistration.cs");
        registration.ShouldContain("services.AddCatalogModule(configuration);");
        registration.ShouldNotContain("MapCatalogEndpoints");
        registration.ShouldNotContain("using EShop.Catalog.Api.Endpoints;");
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
    public async Task AddModule_runs_dotnet_restore()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", @"C:\work\EShop\EShop.slnx", noEndpoints: false);

        _proc.Invocations.ShouldContain(i => i.Command == "dotnet" && i.Arguments == "restore");
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
}
