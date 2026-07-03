namespace Modulus.Cli.Infrastructure;

/// <summary>
/// Guards file-system writes against path-traversal in user input or tampered template paths.
/// </summary>
public static class PathGuard
{
    /// <summary>
    /// Combines <paramref name="baseDirectory"/> with <paramref name="relativePath"/> and verifies
    /// the resolved canonical path stays inside <paramref name="baseDirectory"/>. Resolution is
    /// separator-agnostic ('/' and '\' both count): <c>Path.GetFullPath</c> would treat a
    /// backslash as a literal name character on Linux and prefix already-absolute Windows-style
    /// paths with the current directory, which breaks both the guard and cross-platform tests.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the resolved path escapes <paramref name="baseDirectory"/>.
    /// </exception>
    public static string EnsureContained(string baseDirectory, string relativePath)
    {
        var canonicalBase = PathText.ResolveRelative(baseDirectory, ".").TrimEnd('/') + "/";
        var fullPath = PathText.ResolveRelative(baseDirectory, relativePath);

        // Case-sensitive on Linux (ext4/btrfs/etc.), case-insensitive on Windows (NTFS) and
        // macOS (APFS default). Using OrdinalIgnoreCase on Linux would let `../BaseName/...`
        // pass when baseDirectory is `.../basename` even though they are distinct directories.
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!(fullPath + "/").StartsWith(canonicalBase, comparison))
        {
            throw new InvalidOperationException(
                $"Path traversal detected: '{relativePath}' resolves outside '{baseDirectory}'.");
        }

        // Callers hand this to real file APIs and to tests asserting native-looking paths.
        return fullPath.Replace('/', Path.DirectorySeparatorChar);
    }
}
