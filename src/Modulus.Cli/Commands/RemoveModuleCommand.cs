using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class RemoveModuleCommand
{
    public static Command Create(IFileSystem fileSystem, IProcessRunner processRunner, IConsoleOutput console)
    {
        var moduleNameArg = new Argument<string>("module-name")
        {
            Description = "PascalCase name of the module to remove",
        };

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var confirmOption = new Option<bool>("--confirm")
        {
            Description = "Apply the removal. Without this flag, only a dry run is printed.",
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Proceed even if other modules still reference this module.",
        };

        var command = new Command("remove-module", "Remove a module from an existing Modulus solution")
        {
            moduleNameArg,
            solutionOption,
            confirmOption,
            forceOption,
        };

        command.SetAction(async parseResult =>
        {
            var moduleName = parseResult.GetValue(moduleNameArg)!;
            var solution = parseResult.GetValue(solutionOption);
            var confirm = parseResult.GetValue(confirmOption);
            var force = parseResult.GetValue(forceOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new RemoveModuleHandler(fileSystem, processRunner, console, solutionFinder);
            return await handler.ExecuteAsync(moduleName, solution, confirm, force);
        });

        return command;
    }
}
