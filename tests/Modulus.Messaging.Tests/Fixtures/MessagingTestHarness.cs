using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Modulus.Messaging.Tests.Fixtures;

/// <summary>
/// Builds the provider and runs registered hosted services (consumer host, outbox processor)
/// for the duration of a test, stopping them in reverse order on dispose — the same lifecycle
/// a real host provides.
/// </summary>
public sealed class MessagingTestHarness : IAsyncDisposable
{
    private readonly List<IHostedService> _started = [];

    private MessagingTestHarness(ServiceProvider provider) => Provider = provider;

    public ServiceProvider Provider { get; }

    public static async Task<MessagingTestHarness> StartAsync(IServiceCollection services)
    {
        var harness = new MessagingTestHarness(services.BuildServiceProvider());

        foreach (var hostedService in harness.Provider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
            harness._started.Add(hostedService);
        }

        return harness;
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = _started.Count - 1; i >= 0; i--)
            await _started[i].StopAsync(CancellationToken.None);

        await Provider.DisposeAsync();
    }
}
