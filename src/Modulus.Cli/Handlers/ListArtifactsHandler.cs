using System.Text.Json;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Handlers;

/// <summary>
/// A scaffolded artifact kind locatable by path convention inside each module:
/// <c>src/Modules/{Module}/src/{Module}{ProjectSuffix}/{Folder}/{FilePattern}</c>.
/// </summary>
public sealed record ArtifactConvention(string Kind, string ProjectSuffix, string Folder, string FilePattern)
{
    public static readonly ArtifactConvention Events =
        new("integration events", ".Integration", "IntegrationEvents", "*.cs");

    public static readonly ArtifactConvention Consumers =
        new("integration event handlers", ".Infrastructure", "IntegrationEventHandlers", "*Handler.cs");

    public static readonly ArtifactConvention Entities =
        new("entities", ".Domain", "Entities", "*.cs");
}

/// <summary>
/// Lists scaffolded artifacts by directory convention — a pure filesystem scan, mirroring
/// where the add-event / add-consumer / add-entity commands write.
/// </summary>
public sealed class ListArtifactsHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    private sealed record ArtifactRow(string Module, string Name, string Path);

    public int Execute(ArtifactConvention convention, string? solutionPath, bool json)
    {
        var slnxPath = solutionFinder.ResolveSolutionPath(solutionPath, fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError("Could not find a solution file. Use --solution to specify the path, or run from within a Modulus solution directory.");
            return 1;
        }

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))!;
        var modulesDir = Path.Combine(solutionRoot, "src", "Modules");

        var rows = new List<ArtifactRow>();

        if (fileSystem.DirectoryExists(modulesDir))
        {
            foreach (var moduleDir in fileSystem.GetDirectories(modulesDir))
            {
                var moduleName = fileSystem.GetFileName(moduleDir);
                var artifactDir = Path.Combine(
                    moduleDir, "src", $"{moduleName}{convention.ProjectSuffix}", convention.Folder);

                if (!fileSystem.DirectoryExists(artifactDir))
                    continue;

                foreach (var file in fileSystem.GetFiles(artifactDir, convention.FilePattern, SearchOption.TopDirectoryOnly))
                {
                    rows.Add(new ArtifactRow(
                        moduleName,
                        PathText.GetFileNameWithoutExtension(file),
                        PathText.GetRelativePath(solutionRoot, file)));
                }
            }
        }

        if (json)
        {
            console.WriteLine(JsonSerializer.Serialize(
                rows.Select(r => new { module = r.Module, name = r.Name, path = r.Path }),
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (rows.Count == 0)
        {
            console.WriteLine($"No {convention.Kind} found.");
            return 0;
        }

        console.WriteLine($"{"Module",-20} {"Name",-40} Path");
        console.WriteLine(new string('-', 100));
        foreach (var row in rows.OrderBy(r => r.Module).ThenBy(r => r.Name))
        {
            console.WriteLine($"{row.Module,-20} {row.Name,-40} {row.Path}");
        }

        return 0;
    }
}
