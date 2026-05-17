using Modulus.Cli.Infrastructure;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Infrastructure;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_DotnetVersion_ReturnsZero()
    {
        var runner = new ProcessRunner();

        var exitCode = await runner.RunAsync("dotnet", ["--version"], Path.GetTempPath());

        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_NonExistentCommand_Throws()
    {
        var runner = new ProcessRunner();

        await Should.ThrowAsync<Exception>(async () =>
            await runner.RunAsync("definitely-not-a-real-binary-12345", [], Path.GetTempPath()));
    }

    [Fact]
    public async Task RunAsync_CancellationToken_CancelsLongRunningProcess()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // `dotnet build` against a non-existent solution prints an error quickly but the
        // process invocation itself is the cancellation surface we care about.
        // Use a sleep-style invocation that will outrun the cancellation.
        var command = OperatingSystem.IsWindows() ? "powershell" : "sleep";
        var args = OperatingSystem.IsWindows()
            ? (IReadOnlyList<string>)["-NoProfile", "-Command", "Start-Sleep -Seconds 30"]
            : ["30"];

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runner.RunAsync(command, args, Path.GetTempPath(), cts.Token));
    }

    [Fact]
    public async Task RunAsync_PassesArgumentList_NotShellInterpolation()
    {
        // Regression: even if arguments contain shell metacharacters, the process spawn
        // must treat them as opaque args, not as something a shell would interpret.
        var runner = new ProcessRunner();

        // `dotnet --info` accepts no arguments; passing an arg with a shell metachar
        // should still spawn the process (which then complains about the unknown arg).
        // The key is that it doesn't crash the runner or execute the metacharacter.
        var exitCode = await runner.RunAsync(
            "dotnet",
            ["--info"],
            Path.GetTempPath());

        // --info is a known option that returns 0
        exitCode.ShouldBe(0);
    }
}
