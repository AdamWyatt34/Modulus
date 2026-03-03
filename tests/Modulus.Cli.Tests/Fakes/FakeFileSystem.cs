using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Tests.Fakes;

public sealed class FakeFileSystem : IFileSystem
{
    private const char Sep = '\\';

    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private string _currentDirectory = @"C:\work";

    public void SetCurrentDirectory(string path) => _currentDirectory = path;

    public void SeedFile(string path, string content)
    {
        var normalized = Normalize(path);
        _files[normalized] = content;
        RegisterParentDirectories(normalized);
    }

    public void SeedDirectory(string path)
    {
        var normalized = Normalize(path);
        _directories.Add(normalized);
        RegisterParentDirectories(normalized);
    }

    public void CreateDirectory(string path)
    {
        var normalized = Normalize(path);
        _directories.Add(normalized);
        RegisterParentDirectories(normalized);
    }

    public void WriteAllText(string path, string content)
    {
        var normalized = Normalize(path);
        _files[normalized] = content;
        var dir = GetParentDirectory(normalized);
        if (dir is not null)
            CreateDirectory(dir);
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public string ReadAllText(string path)
    {
        var normalized = Normalize(path);
        return _files.TryGetValue(normalized, out var content)
            ? content
            : throw new FileNotFoundException($"File not found: {path}");
    }

    public string GetCurrentDirectory() => _currentDirectory;

    public IReadOnlyList<string> GetDirectories(string path)
    {
        var normalized = Normalize(path);
        var prefix = normalized + Sep;

        return _directories
            .Where(d =>
            {
                if (!d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return false;
                var remainder = d[prefix.Length..];
                return remainder.Length > 0 && !remainder.Contains(Sep);
            })
            .ToList();
    }

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var normalized = Normalize(path);
        var prefix = normalized + Sep;

        var extension = searchPattern.StartsWith('*') ? searchPattern[1..] : "";

        return _files.Keys
            .Where(f =>
            {
                if (!f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (searchOption == SearchOption.TopDirectoryOnly)
                {
                    var remainder = f[prefix.Length..];
                    if (remainder.Contains(Sep))
                        return false;
                }

                if (!string.IsNullOrEmpty(extension))
                    return f.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

                return true;
            })
            .ToList();
    }

    public string GetFullPath(string path) => Normalize(path);

    public string? GetDirectoryName(string path)
    {
        var normalized = Normalize(path);
        return GetParentDirectory(normalized);
    }

    public string GetFileName(string path)
    {
        var normalized = Normalize(path);
        var lastSep = normalized.LastIndexOf(Sep);
        return lastSep >= 0 ? normalized[(lastSep + 1)..] : normalized;
    }

    public IReadOnlyDictionary<string, string> AllFiles => _files;

    private void RegisterParentDirectories(string normalizedPath)
    {
        var dir = GetParentDirectory(normalizedPath);
        while (dir is not null)
        {
            _directories.Add(dir);
            dir = GetParentDirectory(dir);
        }
    }

    /// <summary>
    /// Platform-agnostic normalization: always use backslash as separator.
    /// Avoids Path.GetFullPath which treats backslashes as literal chars on Linux.
    /// </summary>
    private static string Normalize(string path)
    {
        return path.Replace('/', Sep).TrimEnd(Sep);
    }

    /// <summary>
    /// Platform-agnostic parent directory: always split on backslash.
    /// Avoids Path.GetDirectoryName which doesn't recognize backslash on Linux.
    /// </summary>
    private static string? GetParentDirectory(string normalizedPath)
    {
        var lastSep = normalizedPath.LastIndexOf(Sep);
        if (lastSep <= 0)
            return null;
        // Keep "C:" for paths like "C:\work" → "C:"
        return normalizedPath[..lastSep];
    }
}
