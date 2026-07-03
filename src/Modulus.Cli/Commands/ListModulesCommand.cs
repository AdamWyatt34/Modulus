using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class ListModulesCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit results as JSON instead of a table.",
        };

        var command = new Command("list-modules", "List all modules in the current solution")
        {
            jsonOption,
        };

        command.SetAction(parseResult =>
        {
            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new ListModulesHandler(fileSystem, console, solutionFinder);
            return handler.Execute(parseResult.GetValue(jsonOption));
        });

        return command;
    }
}
