namespace Modulus.Cli.Infrastructure;

public sealed class FileSystem : IFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public IReadOnlyList<string> GetDirectories(string path) =>
        Directory.Exists(path) ? Directory.GetDirectories(path) : [];

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) =>
        Directory.Exists(path) ? Directory.GetFiles(path, searchPattern, searchOption) : [];

    public string GetCurrentDirectory() => Directory.GetCurrentDirectory();

    public string GetFullPath(string path) => Path.GetFullPath(path);

    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);

    public string GetFileName(string path) => Path.GetFileName(path);
}
