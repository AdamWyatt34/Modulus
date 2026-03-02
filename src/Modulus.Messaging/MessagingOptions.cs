using System.Reflection;

namespace Modulus.Messaging;

/// <summary>
/// Configuration options for the Modulus messaging infrastructure.
/// </summary>
public sealed class MessagingOptions
{
    /// <summary>Gets or sets the message transport provider. Defaults to <see cref="Messaging.Transport.InMemory"/>.</summary>
    public Transport Transport { get; set; } = Transport.InMemory;

    /// <summary>Gets or sets the connection string for the transport. Required for <see cref="Messaging.Transport.RabbitMq"/> and <see cref="Messaging.Transport.AzureServiceBus"/>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Gets the list of assemblies to scan for <see cref="Abstractions.IIntegrationEventHandler{TEvent}"/> implementations.</summary>
    public List<Assembly> Assemblies { get; } = [];

    /// <summary>Gets or sets how frequently the outbox processor polls for pending messages. Defaults to 5 seconds.</summary>
    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the maximum number of outbox messages to process per poll cycle. Defaults to 100.</summary>
    public int OutboxBatchSize { get; set; } = 100;
}
