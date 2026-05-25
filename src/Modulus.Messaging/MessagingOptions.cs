using System.Reflection;
using Azure.Core;

namespace Modulus.Messaging;

/// <summary>
/// Configuration options for the Modulus messaging infrastructure.
/// </summary>
public sealed class MessagingOptions
{
    /// <summary>The configuration section name bound by the <c>IConfiguration</c> overload; matches the section the CLI scaffolds.</summary>
    public const string SectionName = "Messaging";

    /// <summary>Gets or sets the message transport provider. Defaults to <see cref="Messaging.Transport.InMemory"/>.</summary>
    public Transport Transport { get; set; } = Transport.InMemory;

    /// <summary>Gets or sets the connection string for the transport. Required for <see cref="Messaging.Transport.RabbitMq"/>, and for <see cref="Messaging.Transport.AzureServiceBus"/> when <see cref="Credential"/> is not set.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the fully-qualified Azure Service Bus namespace (e.g., <c>myns.servicebus.windows.net</c>).
    /// Required when <see cref="Credential"/> is set for the <see cref="Messaging.Transport.AzureServiceBus"/> transport.
    /// </summary>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>
    /// Gets or sets the Azure credential to authenticate the Azure Service Bus transport.
    /// When provided, <see cref="ConnectionString"/> is ignored and <see cref="FullyQualifiedNamespace"/> is used instead.
    /// Use <c>DefaultAzureCredential</c> for workload identity / managed identity in Azure deployments.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>Gets the list of assemblies to scan for <see cref="Abstractions.IIntegrationEventHandler{TEvent}"/> implementations.</summary>
    public List<Assembly> Assemblies { get; } = [];

    /// <summary>Gets or sets how frequently the outbox processor polls for pending messages. Defaults to 5 seconds.</summary>
    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the maximum number of outbox messages to process per poll cycle. Defaults to 100.</summary>
    public int OutboxBatchSize { get; set; } = 100;

    /// <summary>Gets or sets the retry policy for the outbox processor's publish attempts before a message is dead-lettered.</summary>
    public RetryPolicyOptions RetryPolicy { get; set; } = new();

    /// <summary>
    /// Gets or sets the retry policy applied at the MassTransit consumer endpoint when a handler throws,
    /// independent of <see cref="RetryPolicy"/>. (With the in-memory transport the two layers can compound.)
    /// </summary>
    public RetryPolicyOptions ConsumerRetry { get; set; } = new();
}

/// <summary>
/// Exponential-backoff retry settings. A single instance applies to one role: see
/// <see cref="MessagingOptions.RetryPolicy"/> (outbox dispatch) and
/// <see cref="MessagingOptions.ConsumerRetry"/> (consumer endpoint).
/// </summary>
public sealed class RetryPolicyOptions
{
    /// <summary>Maximum number of attempts before a message is dead-lettered. Defaults to 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Initial backoff interval between retries. Defaults to 1 second.</summary>
    public TimeSpan InitialInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum backoff interval between retries. Defaults to 30 seconds.</summary>
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Amount added to the backoff interval on each attempt. Defaults to 5 seconds.</summary>
    public TimeSpan IntervalIncrement { get; set; } = TimeSpan.FromSeconds(5);
}
