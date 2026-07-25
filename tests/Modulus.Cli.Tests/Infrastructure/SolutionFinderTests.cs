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
    public void FindSolutionFile_multiple_slnx_in_same_directory_is_ambiguous_and_does_not_walk_up()
    {
        var fs = new FakeFileSystem();
        // An unrelated, unambiguous solution one level up must NOT be picked as a fallback —
        // that would silently mask the ambiguity in C:\work.
        fs.SeedFile(@"C:\Ancestor.slnx", "<Solution />");
        fs.SeedFile(@"C:\work\First.slnx", "<Solution />");
        fs.SeedFile(@"C:\work\Second.slnx", "<Solution />");
        var finder = new SolutionFinder(fs);

        var result = finder.FindSolutionFile(@"C:\work");

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveSolutionPath_explicit_slnx_file_that_does_not_exist_returns_null()
    {
        var fs = new FakeFileSystem();
        fs.SetCurrentDirectory(@"C:\work");
        var finder = new SolutionFinder(fs);

        var result = finder.ResolveSolutionPath(@"C:\work\DoesNotExist.slnx", @"C:\work");

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveSolutionPath_explicit_directory_that_does_not_exist_returns_null()
    {
        var fs = new FakeFileSystem();
        fs.SetCurrentDirectory(@"C:\work");
        var finder = new SolutionFinder(fs);

        var result = finder.ResolveSolutionPath(@"C:\work\NoSuchDirectory", @"C:\work");

        result.ShouldBeNull();
    }

    [Fact]
    public void DescribeResolutionFailure_no_solution_option_suggests_solution_flag()
    {
        var fs = new FakeFileSystem();
        var finder = new SolutionFinder(fs);

        finder.DescribeResolutionFailure(null).ShouldContain("--solution");
    }

    [Fact]
    public void DescribeResolutionFailure_explicit_missing_file_names_the_file()
    {
        var fs = new FakeFileSystem();
        fs.SetCurrentDirectory(@"C:\work");
        var finder = new SolutionFinder(fs);

        var message = finder.DescribeResolutionFailure(@"C:\work\Ghost.slnx");

        message.ShouldContain("Ghost.slnx");
        message.ShouldContain("does not exist");
    }

    [Fact]
    public void DescribeResolutionFailure_explicit_missing_directory_names_the_directory()
    {
        var fs = new FakeFileSystem();
        fs.SetCurrentDirectory(@"C:\work");
        var finder = new SolutionFinder(fs);

        var message = finder.DescribeResolutionFailure(@"C:\work\NoSuchDirectory");

        message.ShouldContain("NoSuchDirectory");
        message.ShouldContain("does not exist");
    }

    [Fact]
    public void DescribeResolutionFailure_ambiguous_directory_reports_multiple_files()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(@"C:\work\First.slnx", "<Solution />");
        fs.SeedFile(@"C:\work\Second.slnx", "<Solution />");
        var finder = new SolutionFinder(fs);

        var message = finder.DescribeResolutionFailure(@"C:\work");

        message.ShouldContain("Multiple .slnx files");
    }

    [Fact]
    public void GetSolutionName_returns_filename_without_extension()
    {
        SolutionFinder.GetSolutionName(@"C:\work\EShop.slnx").ShouldBe("EShop");
        SolutionFinder.GetSolutionName(@"C:\work\MyApp.sln").ShouldBe("MyApp");
    }

    [Fact]
    public void IsModulusSolution_returns_true_when_program_file_exists()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(@"C:\work\src\EShop.WebApi\Program.cs", "// content");
        var finder = new SolutionFinder(fs);

        finder.IsModulusSolution(@"C:\work", "EShop").ShouldBeTrue();
    }

    [Fact]
    public void IsModulusSolution_returns_false_when_program_file_missing()
    {
        var fs = new FakeFileSystem();
        fs.SeedDirectory(@"C:\work\src\EShop.WebApi");
        var finder = new SolutionFinder(fs);

        finder.IsModulusSolution(@"C:\work", "EShop").ShouldBeFalse();
    }
}
