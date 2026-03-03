using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Infrastructure;

public class SolutionFinderTests
{
    [Fact]
    public void Finds_slnx_in_current_directory()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(@"C:\work\EShop.slnx", "<Solution />");
        var finder = new SolutionFinder(fs);

        var result = finder.FindSolutionFile(@"C:\work");

        result.ShouldNotBeNull();
        fs.GetFileName(result).ShouldBe("EShop.slnx");
    }

    [Fact]
    public void Finds_slnx_in_parent_directory()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(@"C:\work\EShop.slnx", "<Solution />");
        fs.SeedDirectory(@"C:\work\src\SomeProject");
        var finder = new SolutionFinder(fs);

        var result = finder.FindSolutionFile(@"C:\work\src\SomeProject");

        result.ShouldNotBeNull();
        fs.GetFileName(result).ShouldBe("EShop.slnx");
    }

    [Fact]
    public void Returns_null_when_no_solution_found()
    {
        var fs = new FakeFileSystem();
        fs.SeedDirectory(@"C:\empty");
        var finder = new SolutionFinder(fs);

        var result = finder.FindSolutionFile(@"C:\empty");

        result.ShouldBeNull();
    }

    [Fact]
    public void GetSolutionName_returns_filename_without_extension()
    {
        SolutionFinder.GetSolutionName(@"C:\work\EShop.slnx").ShouldBe("EShop");
        SolutionFinder.GetSolutionName(@"C:\work\MyApp.sln").ShouldBe("MyApp");
    }

    [Fact]
    public void IsModulusSolution_returns_true_when_registration_file_exists()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(@"C:\work\src\EShop.WebApi\ModuleRegistration.cs", "// content");
        var finder = new SolutionFinder(fs);

        finder.IsModulusSolution(@"C:\work", "EShop").ShouldBeTrue();
    }

    [Fact]
    public void IsModulusSolution_returns_false_when_registration_file_missing()
    {
        var fs = new FakeFileSystem();
        fs.SeedDirectory(@"C:\work\src\EShop.WebApi");
        var finder = new SolutionFinder(fs);

        finder.IsModulusSolution(@"C:\work", "EShop").ShouldBeFalse();
    }
}
