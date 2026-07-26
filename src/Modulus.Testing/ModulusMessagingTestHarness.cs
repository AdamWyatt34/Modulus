using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Modulus.Testing;

/// <summary>
/// Builds the service provider from a configured <see cref="IServiceCollection"/> and runs every
/// registered <see cref="IHostedService"/> — the transport consumer host and the outbox
/// processor, when <c>AddModulusMessaging</c> is registered — for the duration of a test,
/// stopping them in reverse order on dispose. Mirrors the lifecycle a real <c>IHost</c> provides,
/// so a test exercises the exact startup/shutdown ordering production runs under: the consumer
/// host subscribes before the outbox processor's first dispatch pass (a message published with
/// no subscriber is dropped), and the outbox processor stops before consumers drain in-flight
/// work on shutdown.
/// </summary>
public sealed class ModulusMessagingTestHarness : IAsyncDisposable
{
    private readonly List<IHostedService> _started = [];

    private ModulusMessagingTestHarness(ServiceProvider provider) => Provider = provider;

    /// <summary>Gets the service provider built from the collection passed to <see cref="StartAsync"/>.</summary>
    public ServiceProvider Provider { get; }

    /// <summary>
    /// Gets the <see cref="TestMessageTransport"/> registered by
    /// <see cref="ServiceCollectionExtensions.AddModulusTestTransport"/>. Throws if that
    /// extension was not called on the collection passed to <see cref="StartAsync"/> — resolving
    /// it explains what to add rather than failing with a generic DI error.
    /// </summary>
    public TestMessageTransport Transport => Provider.GetRequiredService<TestMessageTransport>();

    /// <summary>
    /// Builds <paramref name="services"/> into a <see cref="ServiceProvider"/> and starts every
    /// registered <see cref="IHostedService"/>, in registration order.
    /// </summary>
    /// <param name="services">
    /// The fully configured service collection — typically built with <c>AddModulusMessaging</c>
    /// and <see cref="ServiceCollectionExtensions.AddModulusTestTransport"/>.
    /// </param>
    public static async Task<ModulusMessagingTestHarness> StartAsync(IServiceCollection services)
    {
        var harness = new ModulusMessagingTestHarness(services.BuildServiceProvider());

        foreach (var hostedService in harness.Provider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
            harness._started.Add(hostedService);
        }

        return harness;
    }

    /// <summary>
    /// Stops every hosted service that was started, in reverse of its start order, then disposes
    /// the underlying provider.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        for (var i = _started.Count - 1; i >= 0; i--)
            await _started[i].StopAsync(CancellationToken.None).ConfigureAwait(false);

        await Provider.DisposeAsync().ConfigureAwait(false);
    }
}
