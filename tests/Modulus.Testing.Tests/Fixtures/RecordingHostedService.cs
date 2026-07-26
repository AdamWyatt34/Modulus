using Microsoft.Extensions.Hosting;

namespace Modulus.Testing.Tests.Fixtures;

/// <summary>Appends "start:{name}"/"stop:{name}" to a shared log, so start/stop order across several registered hosted services is observable.</summary>
public sealed class RecordingHostedService(string name, List<string> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        log.Add($"start:{name}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        log.Add($"stop:{name}");
        return Task.CompletedTask;
    }
}
