namespace Modulus.Messaging.Transports;

/// <summary>
/// Creates the <see cref="IMessageTransport"/> for one <see cref="Modulus.Messaging.Transport"/>
/// value. Broker transport packages (ModulusKit.Messaging.RabbitMq,
/// ModulusKit.Messaging.AzureServiceBus) register a factory; core resolves the one matching
/// <see cref="MessagingOptions.Transport"/> at startup.
/// </summary>
public interface ITransportFactory
{
    /// <summary>The transport value this factory provides.</summary>
    Transport Transport { get; }

    /// <summary>Creates the transport. Called once; the result is registered as a singleton.</summary>
    IMessageTransport Create(IServiceProvider provider, MessagingOptions options);
}
