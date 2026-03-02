using System.Diagnostics;

namespace Modulus.Cli.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(string command, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {command}");

        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
