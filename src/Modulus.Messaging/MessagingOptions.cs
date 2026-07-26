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

    /// <summary>
    /// Gets or sets the endpoint identity of this host: the RabbitMQ queue name and the Azure Service Bus
    /// subscription name its consumers receive on. Replicas sharing an endpoint name compete for messages.
    /// Defaults to the entry assembly name, lower-cased and sanitized to broker-safe characters.
    /// </summary>
    public string? EndpointName { get; set; }

    /// <summary>
    /// Gets or sets the number of messages the broker delivers ahead of acknowledgement
    /// (RabbitMQ prefetch / Azure Service Bus concurrent calls and prefetch). Defaults to 10.
    /// </summary>
    public int PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether the transport declares its own topology (exchanges, queues, topics,
    /// subscriptions) at startup and on first publish. Defaults to <c>true</c>. Set to <c>false</c>
    /// for least-privilege deployments where entities are pre-created.
    /// </summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>
    /// Gets or sets the outbox processor's fallback sweep interval. Defaults to 5 seconds.
    /// Rows saved through <see cref="Abstractions.IOutboxStore"/> or a context with
    /// <see cref="Outbox.OutboxNotifyingInterceptor"/> attached are dispatched immediately via
    /// the wake signal; this interval only bounds latency for rows the signal cannot see
    /// (other process instances, external writers, transactions EF Core does not observe).
    /// </summary>
    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the maximum number of outbox messages to process per poll cycle. Defaults to 100.</summary>
    public int OutboxBatchSize { get; set; } = 100;

    /// <summary>Gets or sets the retry policy for the outbox processor's publish attempts before a message is dead-lettered.</summary>
    public RetryPolicyOptions RetryPolicy { get; set; } = new();

    /// <summary>
    /// Gets or sets the in-process retry policy applied by the consumer pipeline when a handler
    /// throws, independent of <see cref="RetryPolicy"/>. When all attempts are exhausted the
    /// message is dead-lettered on the transport.
    /// </summary>
    public RetryPolicyOptions ConsumerRetry { get; set; } = new();

    /// <summary>
    /// Gets or sets how long an inbox consumer reservation may sit unprocessed before another
    /// delivery may take it over (e.g. after the owning process crashed mid-handler). Must
    /// exceed the worst-case handler execution time, or a slow handler and a concurrent
    /// delivery can both execute. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan ConsumerReservationTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the retention policy for delivered outbox rows and aged inbox rows.
    /// Disabled by default — see <see cref="RetentionOptions.Enabled"/>.
    /// </summary>
    public RetentionOptions Retention { get; set; } = new();
}

/// <summary>
/// Retention settings for the messaging stores. When enabled, a background sweep permanently
/// deletes outbox rows that were successfully published more than
/// <see cref="ProcessedOutboxAge"/> ago and inbox rows older than <see cref="InboxAge"/>,
/// bounding table growth that would otherwise degrade the polling and reservation queries.
/// </summary>
public sealed class RetentionOptions
{
    /// <summary>
    /// Gets or sets whether the retention sweep runs. Defaults to <see langword="false"/> —
    /// deleting rows is opt-in, never a surprise of an upgrade.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets how long successfully published outbox rows are kept before being purged.
    /// Unprocessed rows — pending, backing off, or dead-lettered — are never purged by the
    /// sweep. Defaults to 7 days.
    /// </summary>
    public TimeSpan ProcessedOutboxAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets how long inbox rows are kept, measured from the event's
    /// <c>OccurredOn</c> timestamp. Purged rows leave the deduplication window, so this must
    /// exceed the broker's maximum redelivery horizon — dead-letter replays included — or a
    /// late redelivery re-executes handlers. Defaults to 7 days.
    /// </summary>
    public TimeSpan InboxAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Gets or sets how often the retention sweep runs. Defaults to 1 hour.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the maximum rows deleted per round trip. Each sweep repeats batches until
    /// the stores are drained, so this bounds lock footprint, not total throughput.
    /// Defaults to 500.
    /// </summary>
    public int PurgeBatchSize { get; set; } = 500;
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
