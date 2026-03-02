using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class ListModulesHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private ListModulesHandler CreateHandler()
    {
        var solutionFinder = new SolutionFinder(_fs);
        return new ListModulesHandler(_fs, _console, solutionFinder);
    }

    [Fact]
    public void Lists_modules_with_project_counts()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<Solution />");

        _fs.SeedFile(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Domain\Catalog.Domain.csproj", "");
        _fs.SeedFile(@"C:\work\EShop\src\Modules\Catalog\src\Catalog.Application\Catalog.Application.csproj", "");
        _fs.SeedFile(@"C:\work\EShop\src\Modules\Ordering\src\Ordering.Domain\Ordering.Domain.csproj", "");

        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Catalog");
        _fs.SeedDirectory(@"C:\work\EShop\src\Modules\Ordering");

        var handler = CreateHandler();

        var result = handler.Execute();

        result.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("Catalog") && l.Contains("2 projects"));
        _console.Lines.ShouldContain(l => l.Contains("Ordering") && l.Contains("1 projects"));
    }

    [Fact]
    public void Prints_message_when_no_modules_exist()
    {
        _fs.SetCurrentDirectory(@"C:\work\EShop");
        _fs.SeedFile(@"C:\work\EShop\EShop.slnx", "<Solution />");

        var handler = CreateHandler();

        var result = handler.Execute();

        result.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("No modules found"));
    }

    [Fact]
    public void Returns_error_when_solution_not_found()
    {
        _fs.SetCurrentDirectory(@"C:\empty");

        var handler = CreateHandler();

        var result = handler.Execute();

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("Could not find"));
    }
}
