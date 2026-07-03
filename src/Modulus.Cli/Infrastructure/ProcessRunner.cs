using System.Diagnostics;

namespace Modulus.Cli.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // With redirected pipes, `dotnet build`/`restore` children can hang this runner:
        // MSBuild node-reuse workers and the Roslyn compiler server outlive the build,
        // inherit the pipe's write handle, and keep ReadToEnd from ever completing.
        // Disable the daemons for our children (env vars double as MSBuild properties).
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["UseSharedCompilation"] = "false";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {command}");

        // Drain stdout/stderr to prevent pipe buffer deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw;
        }

        // Ensure streams are fully consumed
        await stdoutTask;
        await stderrTask;

        return process.ExitCode;
    }
}
