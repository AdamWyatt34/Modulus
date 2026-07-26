using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Messaging;
using Modulus.Messaging.Transports;

namespace Modulus.Testing;

/// <summary>
/// DI registration for the Modulus.Testing package.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the <see cref="IMessageTransport"/> singleton registered by
    /// <c>AddModulusMessaging</c> with a <see cref="TestMessageTransport"/>, and registers the
    /// same instance under its concrete type so it is resolvable either way (or directly via
    /// <see cref="ModulusMessagingTestHarness.Transport"/>).
    /// </summary>
    /// <remarks>
    /// Must be called after <c>AddModulusMessaging(...)</c> — it replaces that call's transport
    /// registration, so calling it first (or on a collection that never called
    /// <c>AddModulusMessaging</c>) throws <see cref="InvalidOperationException"/>. Works with any
    /// configured <see cref="MessagingOptions.Transport"/> value, though
    /// <see cref="Transport.InMemory"/> is the natural choice — it needs no connection string,
    /// so <c>AddModulusMessaging</c>'s own transport-configuration validation stays satisfied
    /// even though the registered transport is about to be replaced.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <c>AddModulusMessaging(...)</c> was not called on <paramref name="services"/> first.
    /// </exception>
    public static IServiceCollection AddModulusTestTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(MessagingOptions)))
        {
            throw new InvalidOperationException(
                $"{nameof(AddModulusTestTransport)}() must be called after AddModulusMessaging(...): " +
                $"no {nameof(MessagingOptions)} registration was found on the service collection.");
        }

        services.Replace(ServiceDescriptor.Singleton<IMessageTransport>(
            provider => new TestMessageTransport(provider.GetService<MessagingOptions>())));

        // Resolves the exact same singleton instance registered above under its concrete type,
        // rather than constructing a second instance — GetRequiredService<IMessageTransport>()
        // is cached the first time either registration is resolved.
        services.AddSingleton(provider => (TestMessageTransport)provider.GetRequiredService<IMessageTransport>());

        return services;
    }
}
