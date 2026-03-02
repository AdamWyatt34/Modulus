using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class ListModulesCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var command = new Command("list-modules", "List all modules in the current solution");

        command.SetAction(_ =>
        {
            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new ListModulesHandler(fileSystem, console, solutionFinder);
            return handler.Execute();
        });

        return command;
    }
}
