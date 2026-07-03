using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Transports;

namespace Modulus.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusTransportFactory : ITransportFactory
{
    public Transport Transport => Transport.AzureServiceBus;

    public IMessageTransport Create(IServiceProvider provider, MessagingOptions options)
    {
        if (options.Credential is not null)
        {
            if (string.IsNullOrWhiteSpace(options.FullyQualifiedNamespace))
                throw new InvalidOperationException(
                    "FullyQualifiedNamespace is required when Credential is provided for Azure Service Bus.");
        }
        else if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "ConnectionString or Credential + FullyQualifiedNamespace is required for Azure Service Bus transport.");
        }

        return new AzureServiceBusTransport(
            options,
            provider.GetRequiredService<ILogger<AzureServiceBusTransport>>());
    }
}
