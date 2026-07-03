using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.AzureServiceBus;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure Service Bus transport so <c>AddModulusMessaging</c> can activate it
    /// when <see cref="MessagingOptions.Transport"/> is <see cref="Transport.AzureServiceBus"/>.
    /// Call alongside <c>AddModulusMessaging</c>; registration order does not matter.
    /// </summary>
    public static IServiceCollection AddModulusAzureServiceBusTransport(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITransportFactory, AzureServiceBusTransportFactory>());
        return services;
    }
}
