using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.RabbitMq;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the RabbitMQ transport so <c>AddModulusMessaging</c> can activate it when
    /// <see cref="MessagingOptions.Transport"/> is <see cref="Transport.RabbitMq"/>.
    /// Call alongside <c>AddModulusMessaging</c>; registration order does not matter.
    /// </summary>
    public static IServiceCollection AddModulusRabbitMqTransport(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITransportFactory, RabbitMqTransportFactory>());
        return services;
    }
}
