using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class AddCommandCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var commandNameArg = new Argument<string>("command-name")
        {
            Description = "PascalCase name of the command to add (e.g. CreateProduct)",
        };

        var moduleOption = new Option<string>("--module")
        {
            Description = "Name of the target module",
            Required = true,
        };
        moduleOption.Aliases.Add("-m");

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var resultTypeOption = new Option<string?>("--result-type")
        {
            Description = "The result type T in Result<T>. Omit for a void command returning Result.",
        };
        resultTypeOption.Aliases.Add("-r");

        var command = new Command("add-command", "Add a new command with handler and validator to an existing module")
        {
            commandNameArg,
            moduleOption,
            solutionOption,
            resultTypeOption,
        };

        command.SetAction(async parseResult =>
        {
            var commandName = parseResult.GetValue(commandNameArg)!;
            var moduleName = parseResult.GetValue(moduleOption)!;
            var solution = parseResult.GetValue(solutionOption);
            var resultType = parseResult.GetValue(resultTypeOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new AddCommandHandler(fileSystem, console, solutionFinder);
            return await handler.ExecuteAsync(commandName, moduleName, solution, resultType);
        });

        return command;
    }
}
