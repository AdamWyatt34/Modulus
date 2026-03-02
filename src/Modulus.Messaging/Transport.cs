namespace Modulus.Messaging;

/// <summary>
/// Specifies the message transport provider used by the messaging infrastructure.
/// </summary>
public enum Transport
{
    /// <summary>In-process transport for development and testing. No external broker required.</summary>
    InMemory,

    /// <summary>RabbitMQ transport. Requires a connection string.</summary>
    RabbitMq,

    /// <summary>Azure Service Bus transport. Requires a connection string.</summary>
    AzureServiceBus
}
