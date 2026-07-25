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

        var modulesDir = Path.Combine(solutionRoot, "src", "Modules");
        var moduleDir = PathGuard.EnsureContained(solutionRoot, Path.Combine("src", "Modules", moduleName));

        if (!fileSystem.DirectoryExists(moduleDir))
        {
            console.WriteError($"Module '{moduleName}' was not found at '{moduleDir}'.");
            return 1;
        }

        // The host's own registration reference (wired by `add-module`, see AddModuleHandler) is
        // deliberately excluded from the blocking scan below and from `--force`: it is machinery
        // this tool owns end-to-end, so it is always safe to clean up unconditionally as part of
        // removal, the same way `add-module` added it. Any *other* reference into this module —
        // a sibling module, a root-level test project, BuildingBlocks, anything hand-authored — is
        // a real, human-intended dependency and still requires --force.
        var infrastructureCsprojFileName = $"{moduleName}.Infrastructure.csproj";
        var hostCsprojPath = Path.Combine(solutionRoot, "src", $"{solutionName}.WebApi", $"{solutionName}.WebApi.csproj");
        var hostReferencesModule = fileSystem.FileExists(hostCsprojPath)
            && ProjectReferenceEditor.HasReferenceTo(fileSystem.ReadAllText(hostCsprojPath), infrastructureCsprojFileName);

        var references = FindReferencingProjects(solutionRoot, modulesDir, moduleDir, hostCsprojPath, moduleName);

        if (references.Count > 0 && !force)
        {
            console.WriteError($"Module '{moduleName}' is still referenced by other projects. Pass --force to remove it anyway:");
            foreach (var reference in references)
            {
                console.WriteError($"  {reference.ModuleName} -> {reference.CsprojPath}");
            }

            return 1;
        }

        if (references.Count > 0 && force)
        {
            console.WriteLine("Warning: the following projects reference this module and will be left with broken references:");
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

            if (hostReferencesModule)
            {
                console.WriteLine($"  Remove project reference: {solutionName}.WebApi -> {moduleName}.Infrastructure");
            }

            if (references.Count > 0)
            {
                console.WriteLine("  Cross-project references found (would break without --force):");
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

        if (hostReferencesModule)
        {
            var updatedHostCsproj = ProjectReferenceEditor.RemoveReference(
                fileSystem.ReadAllText(hostCsprojPath), infrastructureCsprojFileName, out var removed);

            if (removed)
            {
                fileSystem.WriteAllText(hostCsprojPath, updatedHostCsproj);
            }
        }

        fileSystem.DeleteDirectory(moduleDir, recursive: true);

        console.WriteSuccess($"Module '{moduleName}' removed successfully.");
        console.WriteLine($"  Projects removed from solution: {csprojPaths.Count}");

        if (hostReferencesModule)
        {
            console.WriteLine($"  Removed project reference: {solutionName}.WebApi -> {moduleName}.Infrastructure");
        }

        console.WriteLine($"  Deleted: {moduleDir}");

        return 0;
    }

    private IReadOnlyList<ModuleReference> FindReferencingProjects(
        string solutionRoot, string modulesDir, string moduleDir, string hostCsprojPath, string moduleName)
    {
        var references = new List<ModuleReference>();

        if (fileSystem.DirectoryExists(modulesDir))
        {
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
        }

        // H-CLI1: a dangling reference from *outside* src/Modules (root tests/, BuildingBlocks,
        // or any other project) breaks the build exactly the same way a sibling module's does, so
        // it must block removal (absent --force) too — today it doesn't, because the scan never
        // looked past src/Modules. The host's own module-registration reference is excluded here;
        // it is unconditionally auto-removed above instead of being gated behind --force.
        foreach (var csproj in fileSystem.GetFiles(solutionRoot, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsWithin(csproj, modulesDir) || PathText.Equals(csproj, hostCsprojPath))
            {
                continue;
            }

            var content = fileSystem.ReadAllText(csproj);
            if (ReferencesModule(content, moduleName))
            {
                references.Add(new ModuleReference(PathText.GetFileNameWithoutExtension(csproj), csproj));
            }
        }

        return references;
    }

    private static bool IsWithin(string path, string directory)
    {
        var normalizedDirectory = directory.Replace('\\', '/').TrimEnd('/') + "/";
        var normalizedPath = path.Replace('\\', '/');
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
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
