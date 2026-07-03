using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.RabbitMq;

internal sealed class RabbitMqTransportFactory : ITransportFactory
{
    public Transport Transport => Transport.RabbitMq;

    public IMessageTransport Create(IServiceProvider provider, MessagingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("ConnectionString is required for RabbitMQ transport.");

        return new RabbitMqTransport(
            options,
            provider.GetRequiredService<ILogger<RabbitMqTransport>>());
    }
}
