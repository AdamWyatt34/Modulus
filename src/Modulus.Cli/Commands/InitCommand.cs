using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class InitCommand
{
    public static Command Create(IFileSystem fileSystem, IProcessRunner processRunner, IConsoleOutput console)
    {
        var solutionNameArg = new Argument<string>("solution-name")
        {
            Description = "PascalCase name of the solution to create",
        };

        var outputOption = new Option<string?>("--output")
        {
            Description = "Output directory (default: current directory)",
        };
        outputOption.Aliases.Add("-o");

        var aspireOption = new Option<bool>("--aspire")
        {
            Description = "Include .NET Aspire AppHost and ServiceDefaults projects",
        };

        var transportOption = new Option<string>("--transport")
        {
            Description = "Messaging transport to pre-configure (inmemory, rabbitmq, azureservicebus)",
            DefaultValueFactory = _ => "inmemory",
        };

        var noGitOption = new Option<bool>("--no-git")
        {
            Description = "Skip git init and initial commit",
        };

        var command = new Command("init", "Scaffold a new modular monolith solution")
        {
            solutionNameArg,
            outputOption,
            aspireOption,
            transportOption,
            noGitOption,
        };

        command.SetAction(async parseResult =>
        {
            var solutionName = parseResult.GetValue(solutionNameArg)!;
            var output = parseResult.GetValue(outputOption);
            var aspire = parseResult.GetValue(aspireOption);
            var transport = parseResult.GetValue(transportOption)!;
            var noGit = parseResult.GetValue(noGitOption);

            if (transport is not ("inmemory" or "rabbitmq" or "azureservicebus"))
            {
                console.WriteError($"Invalid transport '{transport}'. Valid values: inmemory, rabbitmq, azureservicebus.");
                return 1;
            }

            var handler = new InitHandler(fileSystem, processRunner, console);
            return await handler.ExecuteAsync(
                solutionName,
                output ?? fileSystem.GetCurrentDirectory(),
                aspire,
                transport,
                noGit);
        });

        return command;
    }
}
