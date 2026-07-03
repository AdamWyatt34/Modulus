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

    [Fact]
    public async Task Init_Aspire_RabbitMq_Then_Build_Succeeds()
    {
        using var temp = new TempDirectory("modulus-e2e-aspire-rmq");

        var fileSystem = new FileSystem();
        var processRunner = new ProcessRunner();
        var console = new ConsoleOutput();

        const string solutionName = "Sample";

        var initHandler = new InitHandler(fileSystem, processRunner, console);
        var initExit = await initHandler.ExecuteAsync(
            solutionName: solutionName,
            outputDirectory: temp.Path,
            includeAspire: true,
            transport: "rabbitmq",
            noGit: true);
        initExit.ShouldBe(0, "modulus init --aspire --transport rabbitmq should succeed");

        var solutionRoot = Path.Combine(temp.Path, solutionName);
        var appHostProgram = Path.Combine(
            solutionRoot, "aspire", $"{solutionName}.AppHost", "Program.cs");
        File.ReadAllText(appHostProgram).ShouldContain("AddRabbitMQ");

        var slnxPath = Path.Combine(solutionRoot, $"{solutionName}.slnx");
        var buildExit = await processRunner.RunAsync(
            "dotnet",
            ["build", slnxPath, "--configuration", "Release", "--nologo"],
            solutionRoot);

        buildExit.ShouldBe(0, "aspire + rabbitmq scaffold should build cleanly");
    }

    [Fact]
    public async Task Init_AddModules_AddEvent_AddConsumer_Then_Build_Succeeds()
    {
        using var temp = new TempDirectory("modulus-e2e-messaging");

        var fileSystem = new FileSystem();
        var processRunner = new ProcessRunner();
        var console = new ConsoleOutput();
        var solutionFinder = new SolutionFinder(fileSystem);

        const string solutionName = "Sample";
        const string sourceModule = "Orders";
        const string consumingModule = "Shipping";

        var initHandler = new InitHandler(fileSystem, processRunner, console);
        var initExit = await initHandler.ExecuteAsync(
            solutionName: solutionName,
            outputDirectory: temp.Path,
            includeAspire: false,
            transport: "inmemory",
            noGit: true);
        initExit.ShouldBe(0, "modulus init should succeed");

        var solutionRoot = Path.Combine(temp.Path, solutionName);
        var slnxPath = Path.Combine(solutionRoot, $"{solutionName}.slnx");

        var addModuleHandler = new AddModuleHandler(fileSystem, processRunner, console, solutionFinder);
        (await addModuleHandler.ExecuteAsync(sourceModule, slnxPath, noEndpoints: false))
            .ShouldBe(0, "add-module for the source module should succeed");
        (await addModuleHandler.ExecuteAsync(consumingModule, slnxPath, noEndpoints: false))
            .ShouldBe(0, "add-module for the consuming module should succeed");

        // Define an integration event in the source module...
        var addEventHandler = new AddEventHandler(fileSystem, console, solutionFinder);
        var addEventExit = await addEventHandler.ExecuteAsync(
            eventName: "OrderShipped",
            moduleName: sourceModule,
            solutionPath: slnxPath,
            properties: "OrderId:Guid,ShippedOn:DateTime");
        addEventExit.ShouldBe(0, "add-event should succeed");

        // ...and consume it from another module (auto-wires the cross-module reference).
        var addConsumerHandler = new AddConsumerHandler(fileSystem, console, solutionFinder);
        var addConsumerExit = await addConsumerHandler.ExecuteAsync(
            eventName: "OrderShipped",
            moduleName: consumingModule,
            solutionPath: slnxPath,
            eventModule: null);
        addConsumerExit.ShouldBe(0, "add-consumer should succeed");

        var handlerPath = Path.Combine(
            solutionRoot, "src", "Modules", consumingModule, "src",
            $"{consumingModule}.Infrastructure", "IntegrationEventHandlers", "OrderShippedHandler.cs");
        File.Exists(handlerPath).ShouldBeTrue($"expected consumer at {handlerPath}");

        var consumingInfraCsproj = Path.Combine(
            solutionRoot, "src", "Modules", consumingModule, "src",
            $"{consumingModule}.Infrastructure", $"{consumingModule}.Infrastructure.csproj");
        File.ReadAllText(consumingInfraCsproj)
            .ShouldContain($"{sourceModule}.Integration.csproj");

        var buildExit = await processRunner.RunAsync(
            "dotnet",
            ["build", slnxPath, "--configuration", "Release", "--nologo"],
            solutionRoot);

        buildExit.ShouldBe(0, "solution with a scaffolded event and consumer should build cleanly");
    }
}
