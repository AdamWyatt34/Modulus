using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;

namespace Modulus.Cli.Handlers;

/// <summary>
/// Wraps <c>dotnet ef migrations add</c> with the solution's own conventions: the module's
/// Infrastructure project carries the DbContext and receives the migration
/// (<c>--project</c>), and the WebApi host is the design-time startup project
/// (<c>--startup-project</c>) — its generated <c>AddAllModules</c> registers the module's
/// context, so no <c>IDesignTimeDbContextFactory</c> is needed and no database is contacted.
/// </summary>
public sealed class AddMigrationHandler(
    IFileSystem fileSystem,
    IProcessRunner processRunner,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public async Task<int> ExecuteAsync(
        string migrationName,
        string moduleName,
        string? solutionPath,
        string? context = null,
        string outputDir = "Migrations",
        bool dryRun = false)
    {
        if (!CSharpIdentifierValidator.IsValid(migrationName))
        {
            console.WriteError($"'{migrationName}' is not a valid migration name. Use PascalCase with letters, digits, and underscores (e.g. AddOrderTable).");
            return 1;
        }

        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return 1;
        }

        var slnxPath = solutionFinder.ResolveSolutionPath(solutionPath, fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError(solutionFinder.DescribeResolutionFailure(solutionPath));
            return 1;
        }

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))
            ?? throw new InvalidOperationException($"Could not determine directory for path: {slnxPath}");
        var solutionName = SolutionFinder.GetSolutionName(slnxPath);

        if (!solutionFinder.IsModulusSolution(solutionRoot, solutionName))
        {
            console.WriteError($"The solution at '{solutionRoot}' does not appear to be a Modulus solution (Program.cs not found in {solutionName}.WebApi).");
            return 1;
        }

        var moduleDir = Path.Combine(solutionRoot, "src", "Modules", moduleName);
        if (!fileSystem.DirectoryExists(moduleDir))
        {
            console.WriteError($"Module '{moduleName}' not found at '{moduleDir}'. Run 'modulus list-modules' to see available modules.");
            return 1;
        }

        var infrastructureCsproj = Path.Combine(
            moduleDir, "src", $"{moduleName}.Infrastructure", $"{moduleName}.Infrastructure.csproj");
        if (!fileSystem.FileExists(infrastructureCsproj))
        {
            console.WriteError($"Module Infrastructure project not found at '{infrastructureCsproj}'. Migrations live in the module's Infrastructure project.");
            return 1;
        }

        var hostCsproj = Path.Combine(
            solutionRoot, "src", $"{solutionName}.WebApi", $"{solutionName}.WebApi.csproj");
        if (!fileSystem.FileExists(hostCsproj))
        {
            console.WriteError($"Host project not found at '{hostCsproj}'. The WebApi host is required as the design-time startup project.");
            return 1;
        }

        var effectiveContext = string.IsNullOrWhiteSpace(context) ? $"{moduleName}DbContext" : context;

        var arguments = new[]
        {
            "ef", "migrations", "add", migrationName,
            "--project", infrastructureCsproj,
            "--startup-project", hostCsproj,
            "--context", effectiveContext,
            "--output-dir", outputDir,
        };

        if (dryRun)
        {
            console.WriteLine("Dry run - no processes will be started.");
            console.WriteLine("");
            console.WriteLine("Would run (from " + solutionRoot + "):");
            console.WriteLine($"  dotnet {string.Join(' ', arguments)}");
            console.WriteLine("");
            console.WriteLine($"The migration would be written to '{Path.Combine(fileSystem.GetDirectoryName(infrastructureCsproj)!, outputDir)}'.");
            return 0;
        }

        var exitCode = await processRunner.RunAsync("dotnet", arguments, solutionRoot);

        if (exitCode != 0)
        {
            console.WriteError(
                $"'dotnet ef migrations add' failed with exit code {exitCode}. Common causes: " +
                "the dotnet-ef tool is not installed (fix: dotnet tool install --global dotnet-ef), " +
                $"the solution does not build, or the context '{effectiveContext}' is not registered by the module. " +
                "Re-run the printed command manually to see the full dotnet-ef output.");
            console.WriteLine($"  dotnet {string.Join(' ', arguments)}");
            return 1;
        }

        console.WriteSuccess(
            $"Migration '{migrationName}' added for {effectiveContext} in '{Path.Combine("src", "Modules", moduleName, "src", $"{moduleName}.Infrastructure", outputDir)}'.");
        console.WriteLine(
            "Apply it at startup with context.Database.MigrateAsync() (or 'dotnet ef database update' with the same --project/--startup-project). " +
            "The read-only context maps the same model and never gets its own migrations.");
        return 0;
    }
}
