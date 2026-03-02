using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class AddModuleCommand
{
    public static Command Create(IFileSystem fileSystem, IProcessRunner processRunner, IConsoleOutput console)
    {
        var moduleNameArg = new Argument<string>("module-name")
        {
            Description = "PascalCase name of the module to add",
        };

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var noEndpointsOption = new Option<bool>("--no-endpoints")
        {
            Description = "Skip creating the Api project (for backend-only modules)",
        };

        var command = new Command("add-module", "Add a new module to an existing Modulus solution")
        {
            moduleNameArg,
            solutionOption,
            noEndpointsOption,
        };

        command.SetAction(async parseResult =>
        {
            var moduleName = parseResult.GetValue(moduleNameArg)!;
            var solution = parseResult.GetValue(solutionOption);
            var noEndpoints = parseResult.GetValue(noEndpointsOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new AddModuleHandler(fileSystem, processRunner, console, solutionFinder);
            return await handler.ExecuteAsync(moduleName, solution, noEndpoints);
        });

        return command;
    }
}
