using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Tests.Fakes;

public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private string _currentDirectory = @"C:\work";

    public void SetCurrentDirectory(string path) => _currentDirectory = path;

    public void SeedFile(string path, string content)
    {
        var normalized = Normalize(path);
        _files[normalized] = content;
        // Also register all parent directories
        var dir = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(dir))
        {
            _directories.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    public void SeedDirectory(string path)
    {
        var normalized = Normalize(path);
        _directories.Add(normalized);
        var dir = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(dir))
        {
            _directories.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    public void CreateDirectory(string path)
    {
        var normalized = Normalize(path);
        _directories.Add(normalized);
        var dir = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(dir))
        {
            _directories.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    public void WriteAllText(string path, string content)
    {
        var normalized = Normalize(path);
        _files[normalized] = content;
        var dir = Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(dir))
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
        var normalizedWithSep = normalized.EndsWith(Path.DirectorySeparatorChar)
            ? normalized
            : normalized + Path.DirectorySeparatorChar;

        return _directories
            .Where(d =>
            {
                if (!d.StartsWith(normalizedWithSep, StringComparison.OrdinalIgnoreCase))
                    return false;
                var remainder = d[normalizedWithSep.Length..];
                return remainder.Length > 0 && !remainder.Contains(Path.DirectorySeparatorChar);
            })
            .ToList();
    }

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var normalized = Normalize(path);
        var normalizedWithSep = normalized.EndsWith(Path.DirectorySeparatorChar)
            ? normalized
            : normalized + Path.DirectorySeparatorChar;

        var extension = searchPattern.StartsWith('*') ? searchPattern[1..] : "";

        return _files.Keys
            .Where(f =>
            {
                if (!f.StartsWith(normalizedWithSep, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (searchOption == SearchOption.TopDirectoryOnly)
                {
                    var remainder = f[normalizedWithSep.Length..];
                    if (remainder.Contains(Path.DirectorySeparatorChar))
                        return false;
                }

                if (!string.IsNullOrEmpty(extension))
                    return f.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

                return true;
            })
            .ToList();
    }

    public IReadOnlyDictionary<string, string> AllFiles => _files;

    private static string Normalize(string path) => Path.GetFullPath(path);
}
