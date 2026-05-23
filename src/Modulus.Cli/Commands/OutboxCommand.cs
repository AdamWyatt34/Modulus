using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class OutboxCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var connectionStringOption = new Option<string?>("--connection-string")
        {
            Description = "Database connection string. If omitted, read from Messaging:ConnectionString in --config (or ./appsettings.json).",
        };

        var configOption = new Option<string?>("--config")
        {
            Description = "Path to appsettings.json (default: ./appsettings.json in the current directory).",
        };

        var providerOption = new Option<OutboxProvider>("--provider")
        {
            Description = "EF Core provider for the outbox database.",
            DefaultValueFactory = _ => OutboxProvider.SqlServer,
        };

        var handler = new OutboxHandler(fileSystem, console, OutboxStoreFactory.Create);

        var listFailed = new Command("list-failed", "List dead-lettered outbox messages whose attempt count exceeds the retry threshold.");
        var maxAttemptsOption = new Option<int>("--max-attempts")
        {
            Description = "Threshold above which a message is considered dead-lettered. Should match MessagingOptions.RetryPolicy.MaxAttempts (default: 5).",
            DefaultValueFactory = _ => 5,
        };
        listFailed.Options.Add(connectionStringOption);
        listFailed.Options.Add(configOption);
        listFailed.Options.Add(providerOption);
        listFailed.Options.Add(maxAttemptsOption);
        listFailed.SetAction(async parseResult =>
        {
            var connection = handler.ResolveConnection(
                parseResult.GetValue(connectionStringOption),
                parseResult.GetValue(configOption),
                parseResult.GetValue(providerOption));

            if (connection is null)
                return 1;

            return await handler.ListFailedAsync(connection, parseResult.GetValue(maxAttemptsOption));
        });

        var messageIdArgument = new Argument<Guid>("messageId")
        {
            Description = "The outbox message identifier (Guid).",
        };

        var retry = new Command("retry", "Reset attempt count and clear the last-error so the outbox processor will retry the message.");
        retry.Arguments.Add(messageIdArgument);
        retry.Options.Add(connectionStringOption);
        retry.Options.Add(configOption);
        retry.Options.Add(providerOption);
        retry.SetAction(async parseResult =>
        {
            var connection = handler.ResolveConnection(
                parseResult.GetValue(connectionStringOption),
                parseResult.GetValue(configOption),
                parseResult.GetValue(providerOption));

            if (connection is null)
                return 1;

            return await handler.RetryAsync(connection, parseResult.GetValue(messageIdArgument));
        });

        var purge = new Command("purge", "Permanently delete an outbox message.");
        purge.Arguments.Add(messageIdArgument);
        purge.Options.Add(connectionStringOption);
        purge.Options.Add(configOption);
        purge.Options.Add(providerOption);
        purge.SetAction(async parseResult =>
        {
            var connection = handler.ResolveConnection(
                parseResult.GetValue(connectionStringOption),
                parseResult.GetValue(configOption),
                parseResult.GetValue(providerOption));

            if (connection is null)
                return 1;

            return await handler.PurgeAsync(connection, parseResult.GetValue(messageIdArgument));
        });

        var outbox = new Command("outbox", "Inspect and operate the transactional outbox.")
        {
            listFailed,
            retry,
            purge,
        };

        return outbox;
    }
}
