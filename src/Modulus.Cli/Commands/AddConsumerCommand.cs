using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class AddConsumerCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var eventNameArg = new Argument<string>("event-name")
        {
            Description = "PascalCase name of the integration event to consume (e.g. OrderShipped)",
        };

        var moduleOption = new Option<string>("--module")
        {
            Description = "Name of the consuming module that will host the handler",
            Required = true,
        };
        moduleOption.Aliases.Add("-m");

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var eventModuleOption = new Option<string?>("--event-module")
        {
            Description = "Name of the module that owns the event. Use to disambiguate when the same event name exists in multiple modules.",
        };

        var command = new Command("add-consumer", "Add an integration event handler to a module and wire the cross-module reference")
        {
            eventNameArg,
            moduleOption,
            solutionOption,
            eventModuleOption,
        };

        command.SetAction(async parseResult =>
        {
            var eventName = parseResult.GetValue(eventNameArg)!;
            var moduleName = parseResult.GetValue(moduleOption)!;
            var solution = parseResult.GetValue(solutionOption);
            var eventModule = parseResult.GetValue(eventModuleOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new AddConsumerHandler(fileSystem, console, solutionFinder);
            return await handler.ExecuteAsync(eventName, moduleName, solution, eventModule);
        });

        return command;
    }
}
