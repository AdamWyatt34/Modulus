namespace Modulus.Cli.Infrastructure;

public interface IFileSystem
{
    void CreateDirectory(string path);
    void WriteAllText(string path, string content);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    IReadOnlyList<string> GetDirectories(string path);
    IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption);
    string GetCurrentDirectory();
}
