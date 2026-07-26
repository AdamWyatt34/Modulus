using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Modulus.Cli.IntegrationTests;

/// <summary>
/// Boots a scaffolded WebApi host from its built DLL (not <c>dotnet run</c> — no MSBuild parent
/// process, so kill-tree is deterministic and launchSettings port pinning doesn't apply), binds
/// it to an ephemeral port, and waits for <c>/healthz</c> to answer before handing control to
/// the test. Captured output rides along in failure messages.
/// </summary>
internal sealed partial class WebApiProcess : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);

    private readonly Process _process;
    private readonly ConcurrentQueue<string> _output = new();

    private WebApiProcess(Process process) => _process = process;

    public string BaseAddress { get; private set; } = string.Empty;

    [GeneratedRegex(@"Now listening on:\s+(http://\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ListeningLine();

    public string CapturedOutput => string.Join('\n', _output);

    /// <summary>Starts the host and waits until it serves <c>/healthz</c>.</summary>
    public static async Task<WebApiProcess> StartAsync(string solutionRoot, string solutionName)
    {
        var dllPath = Path.Combine(
            solutionRoot, "src", $"{solutionName}.WebApi", "bin", "Release", "net10.0", $"{solutionName}.WebApi.dll");

        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Built WebApi host not found at {dllPath}; build the scaffold first.", dllPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(dllPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(dllPath);
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        // Port 0: the OS picks a free port, so parallel E2E workspaces can't collide.
        psi.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the scaffolded WebApi host.");
        var host = new WebApiProcess(process);

        var listeningAddress = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Line-by-line capture on background handlers — the server never exits, so ReadToEnd
        // would hang; a bounded queue keeps diagnostics without unbounded growth.
        process.OutputDataReceived += (_, e) => host.Capture(e.Data, listeningAddress);
        process.ErrorDataReceived += (_, e) => host.Capture(e.Data, listeningAddress: null);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var address = await listeningAddress.Task.WaitAsync(StartupTimeout);
            host.BaseAddress = address;
            await host.WaitForHealthyAsync();
            return host;
        }
        catch (Exception ex)
        {
            await host.DisposeAsync();
            throw new InvalidOperationException(
                $"Scaffolded WebApi host failed to become healthy within {StartupTimeout.TotalSeconds}s: {ex.Message}\n" +
                $"Captured output:\n{host.CapturedOutput}", ex);
        }
    }

    private void Capture(string? line, TaskCompletionSource<string>? listeningAddress)
    {
        if (line is null)
            return;

        if (_output.Count < 500)
            _output.Enqueue(line);

        if (listeningAddress is not null && ListeningLine().Match(line) is { Success: true } match)
            listeningAddress.TrySetResult(match.Groups[1].Value);
    }

    private async Task WaitForHealthyAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseAddress) };
        var deadline = DateTime.UtcNow + StartupTimeout;

        while (true)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"Host exited early with code {_process.ExitCode}.");

            try
            {
                var response = await client.GetAsync("/healthz");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Kestrel not accepting yet; keep polling.
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("/healthz never returned success.");

            await Task.Delay(250);
        }
    }

    public async Task<(int StatusCode, string Body)> GetAsync(string path)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseAddress) };
        var response = await client.GetAsync(path);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                // Bounded: a wedged host must not hang test teardown (or, on Windows, hold the
                // exe file-lock into TempDirectory cleanup).
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await _process.WaitForExitAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Leave it to the OS; TempDirectory.Dispose swallows residual locks.
        }
        finally
        {
            _process.Dispose();
        }
    }
}
