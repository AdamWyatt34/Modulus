using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Shouldly;
using Xunit;

namespace Modulus.Cli.IntegrationTests;

// Run-grade E2E: the audit's core coverage gap was that scaffolds were built but never *run*
// (docs/audit/full-scan-2026-07-25.md §Test coverage gaps). This chain scaffolds a solution
// through every generator command, builds it, BOOTS it, hits real HTTP endpoints, runs
// doctor --strict, and executes the scaffolded test suite.
[Trait("Category", "E2E")]
public class RunGradeE2ETests
{
    [Fact]
    public async Task Scaffold_Run_Curl_Doctor_And_ScaffoldedTests_AllSucceed()
    {
        using var temp = new TempDirectory("modulus-e2e-run");

        var fileSystem = new FileSystem();
        var processRunner = new ProcessRunner();
        var console = new ConsoleOutput();
        var solutionFinder = new SolutionFinder(fileSystem);

        const string solutionName = "Shop";
        const string moduleName = "Catalog";
        var packageVersion = E2EPackageFeed.Configure(temp.Path);

        // ── Scaffold: init + module + every generator command ────────────────
        var initHandler = new InitHandler(fileSystem, processRunner, console);
        (await initHandler.ExecuteAsync(
            solutionName: solutionName,
            outputDirectory: temp.Path,
            includeAspire: false,
            transport: "inmemory",
            noGit: true,
            modulusKitVersion: packageVersion)).ShouldBe(0, "modulus init should succeed");

        var solutionRoot = Path.Combine(temp.Path, solutionName);
        var slnxPath = Path.Combine(solutionRoot, $"{solutionName}.slnx");

        var addModuleHandler = new AddModuleHandler(fileSystem, processRunner, console, solutionFinder);
        (await addModuleHandler.ExecuteAsync(moduleName, slnxPath, noEndpoints: false))
            .ShouldBe(0, "add-module should succeed");

        var addEntityHandler = new AddEntityHandler(fileSystem, console, solutionFinder);
        (await addEntityHandler.ExecuteAsync(
            entityName: "Product",
            moduleName: moduleName,
            solutionPath: slnxPath,
            isAggregate: true,
            idType: "guid",
            properties: "Name:string,Price:decimal,Tags:List<string>"))
            .ShouldBe(0, "add-entity with a generic property type should succeed");

        var addCommandHandler = new AddCommandHandler(fileSystem, console, solutionFinder);
        (await addCommandHandler.ExecuteAsync(
            commandName: "CreateProduct",
            moduleName: moduleName,
            solutionPath: slnxPath,
            resultType: "Guid"))
            .ShouldBe(0, "add-command should succeed");

        var addQueryHandler = new AddQueryHandler(fileSystem, console, solutionFinder);
        (await addQueryHandler.ExecuteAsync(
            queryName: "GetProductNames",
            moduleName: moduleName,
            solutionPath: slnxPath,
            resultType: "List<string>"))
            .ShouldBe(0, "add-query with a generic result type should succeed");

        var addEndpointHandler = new AddEndpointHandler(fileSystem, console, solutionFinder);
        (await addEndpointHandler.ExecuteAsync(
            endpointName: "GetProductNames",
            moduleName: moduleName,
            solutionPath: slnxPath,
            method: "GET",
            route: "/names",
            commandName: null,
            queryName: "GetProductNames",
            resultType: "List<string>"))
            .ShouldBe(0, "add-endpoint wired to the scaffolded query should succeed");

        // Route params forward positionally into the wired command/query constructor, so
        // wiring them to a freshly scaffolded (parameterless) record is documented as
        // requiring a manual edit first — the pure-CLI shape with params is the unwired stub.
        (await addEndpointHandler.ExecuteAsync(
            endpointName: "GetProductById",
            moduleName: moduleName,
            solutionPath: slnxPath,
            method: "GET",
            route: "/products/{id:guid}",
            commandName: null,
            queryName: null,
            resultType: null))
            .ShouldBe(0, "add-endpoint stub with a typed route parameter should succeed");

        // ── Build ────────────────────────────────────────────────────────────
        var (buildExit, buildErrors) = await CapturingProcessRunner.BuildAsync(slnxPath, solutionRoot);
        buildExit.ShouldBe(0, $"scaffold with entity/command/query/endpoint should build cleanly:\n{buildErrors}");

        // ── Run + HTTP assertions ────────────────────────────────────────────
        await using (var host = await WebApiProcess.StartAsync(solutionRoot, solutionName))
        {
            var (healthStatus, _) = await host.GetAsync("/healthz");
            healthStatus.ShouldBe(200, $"/healthz should be healthy. Output:\n{host.CapturedOutput}");

            var (readyStatus, _) = await host.GetAsync("/readyz");
            readyStatus.ShouldBe(200, $"/readyz should be healthy. Output:\n{host.CapturedOutput}");

            // The C2 regression guard: a scaffolded module must actually be discovered and
            // mapped by the host — before the 3.0.0 fix this 404'd silently.
            var (sampleStatus, sampleBody) = await host.GetAsync($"/api/{moduleName.ToLowerInvariant()}/sample");
            sampleStatus.ShouldBe(200, $"the module's sample endpoint should respond. Output:\n{host.CapturedOutput}");
            sampleBody.ShouldContain(moduleName, customMessage: "sample endpoint should mention its module");
        }

        // ── doctor --strict ──────────────────────────────────────────────────
        var doctorHandler = new DoctorHandler(fileSystem, console, solutionFinder);
        var doctorExit = await doctorHandler.ExecuteAsync(slnxPath, json: false, strict: true);
        doctorExit.ShouldBe(0, "doctor --strict should pass on a pristine scaffold");

        // ── Scaffolded tests ─────────────────────────────────────────────────
        // The module integration test project needs Docker (Testcontainers SQL Server), so it
        // is excluded here; unit + architecture + endpoint-shape tests all run. In-process
        // HTTP coverage above already exercises what the integration tests would.
        var (testExit, testOutput) = await CapturingProcessRunner.RunAsync(
            "dotnet",
            ["test", slnxPath, "--configuration", "Release", "--nologo", "--filter", "FullyQualifiedName!~Tests.Integration"],
            solutionRoot);
        testExit.ShouldBe(0, $"scaffolded tests should pass out of the box:\n{Truncate(testOutput)}");
    }

    private static string Truncate(string output)
    {
        var lines = output.Split('\n');
        return lines.Length <= 60 ? output : string.Join('\n', lines.TakeLast(60));
    }
}
