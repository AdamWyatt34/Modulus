namespace Modulus.Cli.Infrastructure;

/// <summary>
/// Guards file-system writes against path-traversal in user input or tampered template paths.
/// </summary>
public static class PathGuard
{
    /// <summary>
    /// Combines <paramref name="baseDirectory"/> with <paramref name="relativePath"/> and verifies
    /// the resolved canonical path stays inside <paramref name="baseDirectory"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the resolved path escapes <paramref name="baseDirectory"/>.
    /// </exception>
    public static string EnsureContained(string baseDirectory, string relativePath)
    {
        var canonicalBase = Path.GetFullPath(baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));

        // Case-sensitive on Linux (ext4/btrfs/etc.), case-insensitive on Windows (NTFS) and
        // macOS (APFS default). Using OrdinalIgnoreCase on Linux would let `../BaseName/...`
        // pass when baseDirectory is `.../basename` even though they are distinct directories.
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!fullPath.StartsWith(canonicalBase, comparison))
        {
            throw new InvalidOperationException(
                $"Path traversal detected: '{relativePath}' resolves outside '{baseDirectory}'.");
        }

        return fullPath;
    }
}
