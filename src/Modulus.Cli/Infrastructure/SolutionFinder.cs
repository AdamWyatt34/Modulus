namespace Modulus.Cli.Infrastructure;

public sealed class SolutionFinder(IFileSystem fileSystem)
{
    public string? FindSolutionFile(string startDirectory)
    {
        var current = startDirectory;
        while (current is not null)
        {
            var slnxFiles = fileSystem.GetFiles(current, "*.slnx", SearchOption.TopDirectoryOnly);
            if (slnxFiles.Count == 1)
                return slnxFiles[0];

            // Also check for .sln files
            if (slnxFiles.Count == 0)
            {
                var slnFiles = fileSystem.GetFiles(current, "*.sln", SearchOption.TopDirectoryOnly);
                if (slnFiles.Count == 1)
                    return slnFiles[0];
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    public static string GetSolutionName(string solutionPath) =>
        Path.GetFileNameWithoutExtension(solutionPath);

    public bool IsModulusSolution(string solutionRoot, string solutionName) =>
        fileSystem.FileExists(
            Path.Combine(solutionRoot, "src", $"{solutionName}.WebApi", "ModuleRegistration.cs"));
}
