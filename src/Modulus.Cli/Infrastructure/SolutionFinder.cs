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

    /// <summary>
    /// Resolves a user-supplied --solution value that may be a directory or a file path
    /// into an actual solution file path, falling back to auto-discovery from cwd.
    /// </summary>
    public string? ResolveSolutionPath(string? solutionPath, string currentDirectory)
    {
        if (solutionPath is not null && fileSystem.DirectoryExists(solutionPath))
        {
            // User explicitly passed a directory — search within it only, never walk up
            var slnxFiles = fileSystem.GetFiles(solutionPath, "*.slnx", SearchOption.AllDirectories);
            if (slnxFiles.Count == 1)
                return slnxFiles[0];

            if (slnxFiles.Count == 0)
            {
                var slnFiles = fileSystem.GetFiles(solutionPath, "*.sln", SearchOption.AllDirectories);
                if (slnFiles.Count == 1)
                    return slnFiles[0];
            }

            return null;
        }

        return solutionPath ?? FindSolutionFile(currentDirectory);
    }

    public static string GetSolutionName(string solutionPath) =>
        Path.GetFileNameWithoutExtension(solutionPath);

    public bool IsModulusSolution(string solutionRoot, string solutionName) =>
        fileSystem.FileExists(
            Path.Combine(solutionRoot, "src", $"{solutionName}.WebApi", "ModuleRegistration.cs"));
}
