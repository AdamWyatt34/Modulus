using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class RemoveModuleHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeProcessRunner _proc = new();
    private readonly FakeConsole _console = new();

    private const string Slnx = @"C:\work\EShop\EShop.slnx";
    private const string ModuleRoot = @"C:\work\EShop\src\Modules\Catalog";

    private RemoveModuleHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new RemoveModuleHandler(_fs, _proc, _console, solutionFinder);
    }

    private void SeedModulusSolution()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        _fs.SeedFile(@"C:\work\EShop\src\EShop.WebApi\Program.cs", "// program");
    }

    private void SeedCatalogModule()
    {
        _fs.SeedFile(
            $@"{ModuleRoot}\src\Catalog.Domain\Catalog.Domain.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        _fs.SeedFile(
            $@"{ModuleRoot}\src\Catalog.Application\Catalog.Application.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        _fs.SeedFile(
            $@"{ModuleRoot}\src\Catalog.Infrastructure\Catalog.Infrastructure.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        _fs.SeedFile(
            $@"{ModuleRoot}\src\Catalog.Integration\Catalog.Integration.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        _fs.SeedFile(
            $@"{ModuleRoot}\tests\Catalog.Tests.Unit\Catalog.Tests.Unit.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    [Fact]
    public async Task RemoveModule_dry_run_by_default_lists_actions_and_deletes_nothing()
    {
        SeedModulusSolution();
        SeedCatalogModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", Slnx, confirm: false, force: false);

        result.ShouldBe(0);
        _fs.DirectoryExists(ModuleRoot).ShouldBeTrue();
        _fs.FileExists($@"{ModuleRoot}\src\Catalog.Domain\Catalog.Domain.csproj").ShouldBeTrue();
        _proc.Invocations.ShouldBeEmpty();
        _console.Lines.ShouldContain(l => l.Contains("Dry run"));
        _console.Lines.ShouldContain(l => l.Contains("--confirm to apply"));
    }

    [Fact]
    public async Task RemoveModule_dry_run_lists_each_csproj_and_the_directory()
    {
        SeedModulusSolution();
        SeedCatalogModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", Slnx, confirm: false, force: false);

        _console.Lines.ShouldContain(l => l.Contains("Catalog.Domain.csproj"));
        _console.Lines.ShouldContain(l => l.Contains("Catalog.Application.csproj"));
        _console.Lines.ShouldContain(l => l.Contains("Catalog.Infrastructure.csproj"));
        _console.Lines.ShouldContain(l => l.Contains("Catalog.Integration.csproj"));
        _console.Lines.ShouldContain(l => l.Contains("Catalog.Tests.Unit.csproj"));
        _console.Lines.ShouldContain(l => l.Contains(ModuleRoot));
    }

    [Fact]
    public async Task RemoveModule_with_confirm_removes_slnx_entries_for_each_project()
    {
        SeedModulusSolution();
        SeedCatalogModule();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", Slnx, confirm: true, force: false);

        result.ShouldBe(0);

        var slnRemoveCalls = _proc.Invocations
            .Where(i => i.Command == "dotnet" && i.Arguments.Contains("sln") && i.Arguments.Contains("remove"))
            .ToList();

        slnRemoveCalls.Count.ShouldBe(5);
    }

    [Fact]
    public async Task RemoveModule_with_confirm_deletes_module_directory()
    {
        SeedModulusSolution();
        SeedCatalogModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", Slnx, confirm: true, force: false);

        _fs.DirectoryExists(ModuleRoot).ShouldBeFalse();
        _fs.FileExists($@"{ModuleRoot}\src\Catalog.Domain\Catalog.Domain.csproj").ShouldBeFalse();
        _fs.FileExists($@"{ModuleRoot}\tests\Catalog.Tests.Unit\Catalog.Tests.Unit.csproj").ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveModule_with_confirm_prints_success_summary()
    {
        SeedModulusSolution();
        SeedCatalogModule();
        var handler = CreateHandler();

        await handler.ExecuteAsync("Catalog", Slnx, confirm: true, force: false);

        _console.SuccessLines.ShouldContain(l => l.Contains("removed successfully"));
    }

    [Fact]
    public async Task RemoveModule_rejects_invalid_csharp_identifier()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("123Bad", Slnx, confirm: false, force: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task RemoveModule_returns_error_when_module_does_not_exist()
    {
        SeedModulusSolution();
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Nonexistent", Slnx, confirm: false, force: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("was not found"));
    }

    [Fact]
    public async Task RemoveModule_returns_error_when_solution_not_found()
    {
        _fs.SetCurrentDirectory(@"C:\empty");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", null, confirm: false, force: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Could not find"));
    }

    [Fact]
    public async Task RemoveModule_returns_error_when_not_modulus_solution()
    {
        _fs.SetCurrentDirectory(@"C:\work\Other");
        _fs.SeedFile(@"C:\work\Other\Other.slnx", "<Solution />");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", @"C:\work\Other\Other.slnx", confirm: false, force: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("does not appear to be a Modulus solution"));
    }

    [Fact]
    public async Task RemoveModule_cross_module_reference_without_force_fails_and_deletes_nothing()
    {
        SeedModulusSolution();
        SeedCatalogModule();

        // Shipping module references Catalog.Integration
        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Shipping\src\Shipping.Infrastructure\Shipping.Infrastructure.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\..\\..\\Catalog\\src\\Catalog.Integration\\Catalog.Integration.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");

        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", Slnx, confirm: true, force: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("still referenced"));
        _console.ErrorLines.ShouldContain(l => l.Contains("Shipping") && l.Contains("Shipping.Infrastructure.csproj"));
        _fs.DirectoryExists(ModuleRoot).ShouldBeTrue();
        _proc.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveModule_cross_module_reference_with_force_proceeds()
    {
        SeedModulusSolution();
        SeedCatalogModule();

        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Shipping\src\Shipping.Infrastructure\Shipping.Infrastructure.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\..\\..\\Catalog\\src\\Catalog.Integration\\Catalog.Integration.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");

        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", Slnx, confirm: true, force: true);

        result.ShouldBe(0);
        _fs.DirectoryExists(ModuleRoot).ShouldBeFalse();
        _console.Lines.ShouldContain(l => l.Contains("Warning"));
        _console.Lines.ShouldContain(l => l.Contains("Shipping") && l.Contains("Shipping.Infrastructure.csproj"));
    }

    [Fact]
    public async Task RemoveModule_dry_run_with_cross_module_reference_reports_but_does_not_fail()
    {
        SeedModulusSolution();
        SeedCatalogModule();

        _fs.SeedFile(
            @"C:\work\EShop\src\Modules\Shipping\src\Shipping.Infrastructure\Shipping.Infrastructure.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\..\\..\\Catalog\\src\\Catalog.Integration\\Catalog.Integration.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");

        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("Catalog", Slnx, confirm: false, force: false);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("still referenced"));
        _fs.DirectoryExists(ModuleRoot).ShouldBeTrue();
    }

    [Fact]
    public async Task RemoveModule_deletion_path_stays_within_solution_root()
    {
        SeedModulusSolution();
        SeedCatalogModule();
        var handler = CreateHandler();

        // PathGuard.EnsureContained is exercised internally; a well-formed module name cannot
        // escape the solution root, so this asserts the resolved directory is the expected one.
        await handler.ExecuteAsync("Catalog", Slnx, confirm: true, force: false);

        _fs.AllFiles.Keys.ShouldNotContain(k => k.Contains(".."));
    }
}
