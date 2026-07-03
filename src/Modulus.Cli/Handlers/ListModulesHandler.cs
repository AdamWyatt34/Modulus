using System.Text.Json;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Handlers;

public sealed class ListModulesHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public int Execute(bool json = false)
    {
        var slnxPath = solutionFinder.FindSolutionFile(fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError("Could not find a solution file. Run from within a Modulus solution directory.");
            return 1;
        }

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))!;
        var modulesDir = Path.Combine(solutionRoot, "src", "Modules");

        var modules = fileSystem.DirectoryExists(modulesDir)
            ? fileSystem.GetDirectories(modulesDir)
                .Select(moduleDir => new
                {
                    name = fileSystem.GetFileName(moduleDir),
                    projects = fileSystem.GetFiles(moduleDir, "*.csproj", SearchOption.AllDirectories).Count,
                })
                .ToList()
            : [];

        if (json)
        {
            console.WriteLine(JsonSerializer.Serialize(modules, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (modules.Count == 0)
        {
            console.WriteLine("No modules found.");
            return 0;
        }

        console.WriteLine("Modules:");
        foreach (var module in modules)
        {
            console.WriteLine($"  {module.name} ({module.projects} projects)");
        }

        return 0;
    }
}
