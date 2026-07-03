using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class AddEventCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var eventNameArg = new Argument<string>("event-name")
        {
            Description = "PascalCase name of the integration event to add (e.g. OrderShipped)",
        };

        var moduleOption = new Option<string>("--module")
        {
            Description = "Name of the module that owns the event",
            Required = true,
        };
        moduleOption.Aliases.Add("-m");

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var propertiesOption = new Option<string?>("--properties")
        {
            Description = "Comma-separated payload properties in Name:Type format (e.g. \"OrderId:Guid,Total:decimal\")",
        };
        propertiesOption.Aliases.Add("-p");

        var command = new Command("add-event", "Add a new integration event to an existing module's Integration project")
        {
            eventNameArg,
            moduleOption,
            solutionOption,
            propertiesOption,
        };

        command.SetAction(async parseResult =>
        {
            var eventName = parseResult.GetValue(eventNameArg)!;
            var moduleName = parseResult.GetValue(moduleOption)!;
            var solution = parseResult.GetValue(solutionOption);
            var properties = parseResult.GetValue(propertiesOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new AddEventHandler(fileSystem, console, solutionFinder);
            return await handler.ExecuteAsync(eventName, moduleName, solution, properties);
        });

        return command;
    }
}
