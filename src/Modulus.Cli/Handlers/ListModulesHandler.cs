using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Handlers;

public sealed class ListModulesHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public int Execute()
    {
        var slnxPath = solutionFinder.FindSolutionFile(fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError("Could not find a solution file. Run from within a Modulus solution directory.");
            return 1;
        }

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))!;
        var modulesDir = Path.Combine(solutionRoot, "src", "Modules");

        if (!fileSystem.DirectoryExists(modulesDir))
        {
            console.WriteLine("No modules found.");
            return 0;
        }

        var modules = fileSystem.GetDirectories(modulesDir);
        if (modules.Count == 0)
        {
            console.WriteLine("No modules found.");
            return 0;
        }

        console.WriteLine("Modules:");
        foreach (var moduleDir in modules)
        {
            var moduleName = fileSystem.GetFileName(moduleDir);
            var csprojCount = fileSystem.GetFiles(moduleDir, "*.csproj", SearchOption.AllDirectories).Count;
            console.WriteLine($"  {moduleName} ({csprojCount} projects)");
        }

        return 0;
    }
}
