namespace Modulus.Cli.Infrastructure;

/// <summary>
/// Separator-agnostic path string helpers. Handlers operate on paths that flow through
/// <see cref="IFileSystem"/>, and tests seed Windows-style paths — on Linux,
/// <c>System.IO.Path</c> treats a backslash as a literal character, so functions like
/// <c>GetFullPath</c> and <c>GetFileName</c> silently misbehave on those fixtures. These
/// helpers treat '/' and '\' as equivalent on every platform.
/// </summary>
public static class PathText
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// Resolves <paramref name="relative"/> against <paramref name="baseDir"/>, collapsing '.'
    /// and '..'. A rooted <paramref name="relative"/> (POSIX absolute or drive-qualified)
    /// resolves on its own root instead of being appended — so an absolute escape stays
    /// visibly outside the base. Result uses '/' separators.
    /// </summary>
    public static string ResolveRelative(string baseDir, string relative)
    {
        var effectiveBase = IsRooted(relative) ? relative : baseDir;
        var segments = new List<string>();

        foreach (var part in effectiveBase.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            segments.Add(part);

        if (!IsRooted(relative))
        {
            foreach (var part in relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".")
                    continue;

                if (part == "..")
                {
                    if (segments.Count > 0)
                        segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(part);
            }
        }

        // A leading '/' (POSIX absolute) must survive; 'C:' drive roots carry themselves.
        var prefix = effectiveBase.Length > 0 && (effectiveBase[0] == '/' || effectiveBase[0] == '\\') ? "/" : "";
        return prefix + string.Join('/', segments);
    }

    /// <summary>POSIX-absolute ('/...') or drive-qualified ('C:...') — either separator style.</summary>
    public static bool IsRooted(string path)
        => path.Length > 0 && (path[0] == '/' || path[0] == '\\')
            || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');

    /// <summary>The final path segment.</summary>
    public static string GetFileName(string path)
    {
        var trimmed = path.TrimEnd(Separators);
        var lastSep = trimmed.LastIndexOfAny(Separators);
        return lastSep >= 0 ? trimmed[(lastSep + 1)..] : trimmed;
    }

    /// <summary>The final path segment without its extension.</summary>
    public static string GetFileNameWithoutExtension(string path)
    {
        var name = GetFileName(path);
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>
    /// <paramref name="path"/> relative to <paramref name="root"/> when it sits underneath it
    /// (separator-insensitive, ordinal-ignore-case); otherwise the path unchanged.
    /// Result uses '/' separators.
    /// </summary>
    public static string GetRelativePath(string root, string path)
    {
        var normalizedRoot = Canonical(root).TrimEnd('/') + "/";
        var normalizedPath = Canonical(path);

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? normalizedPath[normalizedRoot.Length..]
            : normalizedPath;
    }

    /// <summary>Whether two paths are equal ignoring separator style and case.</summary>
    public static bool Equals(string left, string right)
        => string.Equals(Canonical(left), Canonical(right), StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string path) => path.Replace('\\', '/');
}
