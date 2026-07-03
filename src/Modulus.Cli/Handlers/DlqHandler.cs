using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Handlers;

/// <summary>Which broker's dead-letter queue to operate on.</summary>
public enum DlqTransport
{
    RabbitMq,
    AzureServiceBus,
}

/// <summary>
/// Connection parameters for a broker DLQ session. <paramref name="EventTypeName"/> is required
/// for Azure Service Bus (its DLQs are per topic/subscription) and ignored for RabbitMQ (one
/// dead-letter queue per endpoint).
/// </summary>
public sealed record DlqConnection(
    DlqTransport Transport,
    string ConnectionString,
    string EndpointName,
    string? EventTypeName);

/// <summary>One dead-lettered message as shown by <c>modulus dlq list</c>.</summary>
public sealed record DlqMessage(
    string MessageId,
    string EventType,
    DateTimeOffset? EnqueuedAt,
    string? Reason,
    long DeliveryCount);

/// <summary>
/// Transport-specific DLQ access port. Implementations own the broker connection; disposing
/// the browser closes it.
/// </summary>
public interface IDlqBrowser : IAsyncDisposable
{
    /// <summary>Reads up to <paramref name="max"/> dead-lettered messages without removing them.</summary>
    Task<IReadOnlyList<DlqMessage>> ListAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>Re-publishes one message (by MessageId) to its original destination. Returns false when not found.</summary>
    Task<bool> ReplayAsync(string messageId, int max, CancellationToken cancellationToken = default);

    /// <summary>Re-publishes up to <paramref name="max"/> dead-lettered messages. Returns the replayed count.</summary>
    Task<int> ReplayAllAsync(int max, CancellationToken cancellationToken = default);
}

public sealed class DlqHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    Func<DlqConnection, IDlqBrowser> browserFactory)
{
    public async Task<int> ListAsync(DlqConnection connection, int max, CancellationToken cancellationToken = default)
    {
        await using var browser = browserFactory(connection);

        IReadOnlyList<DlqMessage> messages;
        try
        {
            messages = await browser.ListAsync(max, cancellationToken);
        }
        catch (Exception ex)
        {
            console.WriteError($"Failed to read the dead-letter queue: {ex.Message}");
            return 1;
        }

        if (messages.Count == 0)
        {
            console.WriteLine("No dead-lettered messages.");
            return 0;
        }

        console.WriteLine($"Dead-lettered messages (showing up to {max}): {messages.Count}");
        console.WriteLine("");
        console.WriteLine($"{"MessageId",-38} {"EnqueuedAt (UTC)",-21} {"Deliveries",-11} {"EventType",-45} Reason");
        console.WriteLine(new string('-', 140));

        foreach (var msg in messages)
        {
            var enqueued = msg.EnqueuedAt?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            console.WriteLine(
                $"{msg.MessageId,-38} {enqueued,-21} {msg.DeliveryCount,-11} {Truncate(msg.EventType, 45),-45} {msg.Reason ?? "-"}");
        }

        return 0;
    }

    public async Task<int> ReplayAsync(
        DlqConnection connection,
        string? messageId,
        bool all,
        int max,
        CancellationToken cancellationToken = default)
    {
        if (all == (messageId is not null))
        {
            console.WriteError("Specify exactly one of --message-id <id> or --all.");
            return 1;
        }

        await using var browser = browserFactory(connection);

        try
        {
            if (all)
            {
                var replayed = await browser.ReplayAllAsync(max, cancellationToken);
                console.WriteSuccess($"Replayed {replayed} message(s). Handlers that already succeeded are skipped by the inbox; only unfinished handlers re-run.");
                return 0;
            }

            var found = await browser.ReplayAsync(messageId!, max, cancellationToken);
            if (!found)
            {
                console.WriteError($"Message '{messageId}' was not found in the first {max} dead-lettered message(s). Raise --max if the queue is deeper.");
                return 1;
            }

            console.WriteSuccess($"Replayed message '{messageId}'.");
            return 0;
        }
        catch (Exception ex)
        {
            console.WriteError($"Replay failed: {ex.Message}");
            return 1;
        }
    }

    public DlqConnection? ResolveConnection(
        DlqTransport transport,
        string? connectionString,
        string? configPath,
        string? endpointName,
        string? eventTypeName)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(endpointName))
        {
            var config = MessagingConfigReader.Read(fileSystem, console, configPath);

            connectionString = string.IsNullOrWhiteSpace(connectionString)
                ? config?.ConnectionString
                : connectionString;
            endpointName = string.IsNullOrWhiteSpace(endpointName)
                ? config?.EndpointName
                : endpointName;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                console.WriteError("No broker connection string. Pass --connection-string or set Messaging:ConnectionString in appsettings.json.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(endpointName))
            {
                console.WriteError("No endpoint name. Pass --endpoint or set Messaging:EndpointName in appsettings.json (the DLQ is per endpoint).");
                return null;
            }
        }

        if (transport == DlqTransport.AzureServiceBus && string.IsNullOrWhiteSpace(eventTypeName))
        {
            console.WriteError("Azure Service Bus dead-letter queues are per topic/subscription. Pass --event <FullEventTypeName> to select the topic.");
            return null;
        }

        return new DlqConnection(transport, connectionString, endpointName, eventTypeName);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
