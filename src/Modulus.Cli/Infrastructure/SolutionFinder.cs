namespace Modulus.Cli.Infrastructure;

public sealed class SolutionFinder(IFileSystem fileSystem)
{
    public string? FindSolutionFile(string startDirectory)
    {
        string? current = startDirectory;
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

            current = fileSystem.GetDirectoryName(current);
        }

        return null;
    }

    /// <summary>
    /// Resolves a user-supplied --solution value that may be a directory or a file path
    /// into an actual solution file path, falling back to auto-discovery from cwd.
    /// </summary>
    public string? ResolveSolutionPath(string? solutionPath, string currentDirectory)
    {
        if (solutionPath is null)
            return FindSolutionFile(currentDirectory);

        // If it's already a solution file path, use it directly
        if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
            solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            return solutionPath;

        // Otherwise treat it as a directory and search within (including subdirectories)
        if (fileSystem.DirectoryExists(solutionPath))
        {
            var slnxFiles = fileSystem.GetFiles(solutionPath, "*.slnx", SearchOption.AllDirectories);
            if (slnxFiles.Count == 1)
                return slnxFiles[0];

            if (slnxFiles.Count > 1)
                return null; // Multiple solution files found — caller should prompt user to specify --solution

            var slnFiles = fileSystem.GetFiles(solutionPath, "*.sln", SearchOption.AllDirectories);
            if (slnFiles.Count == 1)
                return slnFiles[0];
        }

        return null;
    }

    public static string GetSolutionName(string solutionPath)
    {
        // Handle both forward and backslash separators for cross-platform compatibility
        var lastSep = Math.Max(solutionPath.LastIndexOf('/'), solutionPath.LastIndexOf('\\'));
        var fileName = lastSep >= 0 ? solutionPath[(lastSep + 1)..] : solutionPath;
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 ? fileName[..dotIndex] : fileName;
    }

    public bool IsModulusSolution(string solutionRoot, string solutionName) =>
        fileSystem.FileExists(
            Path.Combine(solutionRoot, "src", $"{solutionName}.WebApi", "Program.cs"));
}
