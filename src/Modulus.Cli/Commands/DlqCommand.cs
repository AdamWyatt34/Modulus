using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class DlqCommand
{
    internal static IDlqBrowser CreateBrowser(DlqConnection connection)
        => connection.Transport switch
        {
            DlqTransport.RabbitMq => new RabbitMqDlqBrowser(connection),
            DlqTransport.AzureServiceBus => new AsbDlqBrowser(connection),
            _ => throw new ArgumentOutOfRangeException(nameof(connection)),
        };

    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var transportOption = new Option<DlqTransport>("--transport")
        {
            Description = "The broker to talk to.",
            Required = true,
        };

        var connectionStringOption = new Option<string?>("--connection-string")
        {
            Description = "Broker connection string. If omitted, read from Messaging:ConnectionString in --config (or ./appsettings.json).",
        };

        var configOption = new Option<string?>("--config")
        {
            Description = "Path to appsettings.json (default: ./appsettings.json in the current directory).",
        };

        var endpointOption = new Option<string?>("--endpoint")
        {
            Description = "Endpoint name whose dead-letter queue to operate on. If omitted, read from Messaging:EndpointName in --config.",
        };

        var eventOption = new Option<string?>("--event")
        {
            Description = "Full event type name (e.g. MyApp.Orders.Integration.OrderPlacedEvent). Required for Azure Service Bus, whose DLQs are per topic/subscription.",
        };

        var maxOption = new Option<int>("--max")
        {
            Description = "Maximum messages to read (list) or examine (replay).",
            DefaultValueFactory = _ => 50,
        };

        var handler = new DlqHandler(fileSystem, console, CreateBrowser);

        DlqConnection? Resolve(ParseResult parseResult) => handler.ResolveConnection(
            parseResult.GetValue(transportOption),
            parseResult.GetValue(connectionStringOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(endpointOption),
            parseResult.GetValue(eventOption));

        var list = new Command("list", "List dead-lettered messages on the broker (RabbitMQ: destructive peek — messages are requeued after reading).");
        list.Options.Add(transportOption);
        list.Options.Add(connectionStringOption);
        list.Options.Add(configOption);
        list.Options.Add(endpointOption);
        list.Options.Add(eventOption);
        list.Options.Add(maxOption);
        list.SetAction(async parseResult =>
        {
            var connection = Resolve(parseResult);
            if (connection is null)
                return 1;

            return await handler.ListAsync(connection, parseResult.GetValue(maxOption));
        });

        var messageIdOption = new Option<string?>("--message-id")
        {
            Description = "Replay a single message by its MessageId (the integration event's EventId).",
        };

        var allOption = new Option<bool>("--all")
        {
            Description = "Replay every dead-lettered message, up to --max.",
        };

        var replay = new Command("replay", "Re-publish dead-lettered messages to their original destination. Handlers that already succeeded are skipped by the inbox.");
        replay.Options.Add(transportOption);
        replay.Options.Add(connectionStringOption);
        replay.Options.Add(configOption);
        replay.Options.Add(endpointOption);
        replay.Options.Add(eventOption);
        replay.Options.Add(maxOption);
        replay.Options.Add(messageIdOption);
        replay.Options.Add(allOption);
        replay.SetAction(async parseResult =>
        {
            var connection = Resolve(parseResult);
            if (connection is null)
                return 1;

            return await handler.ReplayAsync(
                connection,
                parseResult.GetValue(messageIdOption),
                parseResult.GetValue(allOption),
                parseResult.GetValue(maxOption));
        });

        return new Command("dlq", "Inspect and replay broker dead-letter queues.")
        {
            list,
            replay,
        };
    }
}
