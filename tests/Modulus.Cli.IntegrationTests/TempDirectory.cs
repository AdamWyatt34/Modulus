namespace Modulus.Cli.IntegrationTests;

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory(string prefix)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // best-effort cleanup; some files may be locked by the dotnet build server
        }
    }
}
