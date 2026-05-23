using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Shouldly;
using Xunit;

namespace Modulus.Cli.IntegrationTests;

[Trait("Category", "E2E")]
public class InitAddModuleBuildTests
{
    [Fact]
    public async Task Init_Then_AddModule_Then_Build_Succeeds()
    {
        using var temp = new TempDirectory("modulus-e2e");

        var fileSystem = new FileSystem();
        var processRunner = new ProcessRunner();
        var console = new ConsoleOutput();
        var solutionFinder = new SolutionFinder(fileSystem);

        const string solutionName = "Sample";
        const string moduleName = "Catalog";

        var initHandler = new InitHandler(fileSystem, processRunner, console);
        var initExit = await initHandler.ExecuteAsync(
            solutionName: solutionName,
            outputDirectory: temp.Path,
            includeAspire: false,
            transport: "inmemory",
            noGit: true);

        initExit.ShouldBe(0, "modulus init should succeed against a real filesystem");

        var solutionRoot = Path.Combine(temp.Path, solutionName);
        var slnxPath = Path.Combine(solutionRoot, $"{solutionName}.slnx");
        File.Exists(slnxPath).ShouldBeTrue($"expected solution file at {slnxPath}");

        var addModuleHandler = new AddModuleHandler(fileSystem, processRunner, console, solutionFinder);
        var addModuleExit = await addModuleHandler.ExecuteAsync(
            moduleName: moduleName,
            solutionPath: slnxPath,
            noEndpoints: false);

        addModuleExit.ShouldBe(0, "modulus add module should succeed against a real filesystem");

        var moduleRoot = Path.Combine(solutionRoot, "src", "Modules", moduleName);
        Directory.Exists(moduleRoot).ShouldBeTrue($"expected module directory at {moduleRoot}");

        var buildExit = await processRunner.RunAsync(
            "dotnet",
            ["build", slnxPath, "--configuration", "Release", "--nologo"],
            solutionRoot);

        buildExit.ShouldBe(0, "scaffolded solution should build cleanly");
    }
}
