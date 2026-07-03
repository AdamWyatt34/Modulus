using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;

namespace Modulus.Cli.Handlers;

public sealed class RemoveModuleHandler(
    IFileSystem fileSystem,
    IProcessRunner processRunner,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public async Task<int> ExecuteAsync(
        string moduleName,
        string? solutionPath,
        bool confirm,
        bool force)
    {
        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return 1;
        }

        var slnxPath = solutionFinder.ResolveSolutionPath(solutionPath, fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError("Could not find a solution file. Use --solution to specify the path, or run from within a Modulus solution directory.");
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

        var modulesDir = Path.Combine(solutionRoot, "src", "Modules");
        var moduleDir = PathGuard.EnsureContained(solutionRoot, Path.Combine("src", "Modules", moduleName));

        if (!fileSystem.DirectoryExists(moduleDir))
        {
            console.WriteError($"Module '{moduleName}' was not found at '{moduleDir}'.");
            return 1;
        }

        var references = FindReferencingProjects(modulesDir, moduleName);

        if (references.Count > 0 && !force)
        {
            console.WriteError($"Module '{moduleName}' is still referenced by other modules. Pass --force to remove it anyway:");
            foreach (var reference in references)
            {
                console.WriteError($"  {reference.ModuleName} -> {reference.CsprojPath}");
            }

            return 1;
        }

        if (references.Count > 0 && force)
        {
            console.WriteLine("Warning: the following modules reference this module and will be left with broken references:");
            foreach (var reference in references)
            {
                console.WriteLine($"  {reference.ModuleName} -> {reference.CsprojPath}");
            }
        }

        var csprojPaths = fileSystem
            .GetFiles(moduleDir, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!confirm)
        {
            console.WriteLine($"Dry run — pass --confirm to apply.");
            console.WriteLine($"The following actions would be taken to remove module '{moduleName}':");

            foreach (var csproj in csprojPaths)
            {
                console.WriteLine($"  Remove from solution: {csproj}");
            }

            console.WriteLine($"  Delete directory: {moduleDir}");

            if (references.Count > 0)
            {
                console.WriteLine("  Cross-module references found (would break without --force):");
                foreach (var reference in references)
                {
                    console.WriteLine($"    {reference.ModuleName} -> {reference.CsprojPath}");
                }
            }

            return 0;
        }

        var fullSlnxPath = fileSystem.GetFullPath(slnxPath);
        foreach (var csproj in csprojPaths)
        {
            var result = await processRunner.RunAsync(
                "dotnet",
                ["sln", fullSlnxPath, "remove", csproj],
                solutionRoot);

            if (result != 0)
            {
                console.WriteError($"Warning: Failed to remove '{fileSystem.GetFileName(csproj)}' from solution.");
            }
        }

        fileSystem.DeleteDirectory(moduleDir, recursive: true);

        console.WriteSuccess($"Module '{moduleName}' removed successfully.");
        console.WriteLine($"  Projects removed from solution: {csprojPaths.Count}");
        console.WriteLine($"  Deleted: {moduleDir}");

        return 0;
    }

    private IReadOnlyList<ModuleReference> FindReferencingProjects(string modulesDir, string moduleName)
    {
        var references = new List<ModuleReference>();

        if (!fileSystem.DirectoryExists(modulesDir))
        {
            return references;
        }

        var otherModules = fileSystem.GetDirectories(modulesDir)
            .Select(fileSystem.GetFileName)
            .Where(name => !string.Equals(name, moduleName, StringComparison.Ordinal));

        foreach (var otherModule in otherModules)
        {
            var otherModuleDir = Path.Combine(modulesDir, otherModule);

            foreach (var csproj in fileSystem.GetFiles(otherModuleDir, "*.csproj", SearchOption.AllDirectories))
            {
                var content = fileSystem.ReadAllText(csproj);
                if (ReferencesModule(content, moduleName))
                {
                    references.Add(new ModuleReference(otherModule, csproj));
                }
            }
        }

        return references;
    }

    /// <summary>
    /// Checks whether a csproj's content contains a ProjectReference pointing into the named
    /// module (e.g. its Integration project). Matches on the "<ModuleName>." prefix within a
    /// ProjectReference Include path so both ".Integration.csproj" and other module projects are
    /// caught, while avoiding false positives from unrelated modules sharing a prefix.
    /// </summary>
    private static bool ReferencesModule(string csprojContent, string moduleName)
    {
        var lines = csprojContent.Split('\n');
        foreach (var line in lines)
        {
            if (!line.Contains("ProjectReference", StringComparison.Ordinal))
                continue;

            if (ContainsModuleProjectPath(line, moduleName))
                return true;
        }

        return false;
    }

    private static bool ContainsModuleProjectPath(string line, string moduleName)
    {
        // Match path segments like "...\ModuleName\src\ModuleName.Integration\ModuleName.Integration.csproj"
        // or a bare "ModuleName.SomeProject.csproj" reference — both indicate a dependency into
        // the module being removed.
        var normalized = line.Replace('\\', '/');
        var moduleSegment = $"/{moduleName}/";
        var moduleFilePrefix = $"{moduleName}.";

        if (normalized.Contains(moduleSegment, StringComparison.Ordinal))
            return true;

        var lastSlash = normalized.LastIndexOf('/');
        var fileName = lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;

        return fileName.StartsWith(moduleFilePrefix, StringComparison.Ordinal)
            && fileName.Contains(".csproj", StringComparison.Ordinal);
    }

    private sealed record ModuleReference(string ModuleName, string CsprojPath);
}
