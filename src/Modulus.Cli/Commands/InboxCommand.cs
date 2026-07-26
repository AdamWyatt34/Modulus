using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class InboxCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var connectionStringOption = new Option<string?>("--connection-string")
        {
            Description = "Database connection string. If omitted, read from ConnectionStrings:Default in --config (or ./appsettings.json).",
        };

        var configOption = new Option<string?>("--config")
        {
            Description = "Path to appsettings.json (default: ./appsettings.json in the current directory).",
        };

        var providerOption = new Option<OutboxProvider>("--provider")
        {
            Description = "EF Core provider for the inbox database.",
            DefaultValueFactory = _ => OutboxProvider.SqlServer,
        };

        var handler = new InboxHandler(fileSystem, console, InboxStoreFactory.Create);

        var purge = new Command(
            "purge",
            "Bulk-delete inbox messages (and their consumer rows) older than a retention age. Without --confirm, reports what would be deleted.");
        var olderThanDaysOption = new Option<int>("--older-than-days")
        {
            Description = "Purge inbox messages that occurred more than this many days ago (default: 7). " +
                "Purged rows leave the deduplication window — only purge past the broker's maximum redelivery horizon.",
            DefaultValueFactory = _ => 7,
        };
        var batchSizeOption = new Option<int>("--batch-size")
        {
            Description = "Maximum rows deleted per round trip; batches repeat until drained (default: 500).",
            DefaultValueFactory = _ => 500,
        };
        var confirmOption = new Option<bool>("--confirm")
        {
            Description = "Actually delete. Without this flag the command only reports the matching row count.",
        };
        purge.Options.Add(connectionStringOption);
        purge.Options.Add(configOption);
        purge.Options.Add(providerOption);
        purge.Options.Add(olderThanDaysOption);
        purge.Options.Add(batchSizeOption);
        purge.Options.Add(confirmOption);
        purge.SetAction(async parseResult =>
        {
            var connection = handler.ResolveConnection(
                parseResult.GetValue(connectionStringOption),
                parseResult.GetValue(configOption),
                parseResult.GetValue(providerOption));

            if (connection is null)
                return 1;

            return await handler.PurgeAsync(
                connection,
                parseResult.GetValue(olderThanDaysOption),
                parseResult.GetValue(batchSizeOption),
                parseResult.GetValue(confirmOption));
        });

        var inbox = new Command("inbox", "Inspect and operate the consumer inbox.")
        {
            purge,
        };

        return inbox;
    }
}
