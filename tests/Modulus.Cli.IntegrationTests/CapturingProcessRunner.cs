using System.Diagnostics;

namespace Modulus.Cli.IntegrationTests;

/// <summary>
/// Runs a process and keeps its output, so an E2E failure message shows the scaffold's
/// actual restore/build errors instead of a bare exit code.
/// </summary>
internal static class CapturingProcessRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory)
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

        // Same hang guard as the shipped ProcessRunner: keep MSBuild/Roslyn daemons from
        // inheriting the redirected pipe and stalling ReadToEnd after the build exits.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["UseSharedCompilation"] = "false";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {command}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await stdoutTask + await stderrTask;
        return (process.ExitCode, output);
    }

    /// <summary>Builds the solution and returns a Shouldly-friendly failure description.</summary>
    public static async Task<(int ExitCode, string Errors)> BuildAsync(string slnxPath, string workingDirectory)
    {
        var (exitCode, output) = await RunAsync(
            "dotnet",
            ["build", slnxPath, "--configuration", "Release", "--nologo"],
            workingDirectory);

        if (exitCode == 0)
            return (0, string.Empty);

        var errorLines = output
            .Split('\n')
            .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
            .Take(15);

        return (exitCode, string.Join("\n", errorLines));
    }
}
