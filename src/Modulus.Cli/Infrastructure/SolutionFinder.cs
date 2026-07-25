namespace Modulus.Cli.Infrastructure;

public sealed class SolutionFinder(IFileSystem fileSystem)
{
    public string? FindSolutionFile(string startDirectory)
    {
        string? current = startDirectory;
        var depth = 0;
        while (current is not null && depth++ < 20)
        {
            var slnxFiles = fileSystem.GetFiles(current, "*.slnx", SearchOption.TopDirectoryOnly);
            if (slnxFiles.Count == 1)
                return slnxFiles[0];

            if (slnxFiles.Count > 1)
            {
                // Ambiguous: two or more .slnx files sit in the same directory. Continuing to
                // walk up would silently paper over this by potentially resolving to an
                // unrelated ancestor solution instead of surfacing the conflict, so stop here.
                return null;
            }

            // Also check for .sln files
            var slnFiles = fileSystem.GetFiles(current, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Count == 1)
                return slnFiles[0];

            if (slnFiles.Count > 1)
                return null;

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

        solutionPath = fileSystem.GetFullPath(solutionPath);

        // If it's already a solution file path, use it directly — but only when it actually
        // exists. Returning an unchecked path here used to defer the failure to a much less
        // clear downstream error (or an outright exception) instead of a crisp "doesn't exist".
        if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
            solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            return fileSystem.FileExists(solutionPath) ? solutionPath : null;

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

    /// <summary>
    /// Builds a precise, human-readable explanation for why <see cref="ResolveSolutionPath"/>
    /// returned <c>null</c> for the given <paramref name="solutionPath"/> input. Distinguishes an
    /// explicit, wrong <c>--solution</c> value (file doesn't exist, directory doesn't exist,
    /// directory has multiple candidates) from a failed auto-discovery, so a user who already
    /// passed <c>--solution</c> isn't told to pass it again.
    /// </summary>
    public string DescribeResolutionFailure(string? solutionPath)
    {
        if (solutionPath is null)
        {
            return "Could not find a solution file. Use --solution to specify the path, or run from within a Modulus solution directory.";
        }

        var fullPath = fileSystem.GetFullPath(solutionPath);

        if (fullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
            fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return $"The --solution file '{fullPath}' does not exist.";
        }

        if (!fileSystem.DirectoryExists(fullPath))
        {
            return $"The --solution path '{fullPath}' does not exist.";
        }

        var slnxCount = fileSystem.GetFiles(fullPath, "*.slnx", SearchOption.AllDirectories).Count;
        if (slnxCount > 1)
        {
            return $"Multiple .slnx files were found under '{fullPath}'. Point --solution directly at the one to use.";
        }

        return $"No .slnx or .sln file was found under '{fullPath}'.";
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
