using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

/// <summary>Factories for the convention-scan list commands (events, consumers, entities).</summary>
public static class ListArtifactsCommands
{
    public static Command CreateListEvents(IFileSystem fileSystem, IConsoleOutput console)
        => Create(fileSystem, console, "list-events", "List integration events across all modules", ArtifactConvention.Events);

    public static Command CreateListConsumers(IFileSystem fileSystem, IConsoleOutput console)
        => Create(fileSystem, console, "list-consumers", "List integration event handlers across all modules", ArtifactConvention.Consumers);

    public static Command CreateListEntities(IFileSystem fileSystem, IConsoleOutput console)
        => Create(fileSystem, console, "list-entities", "List domain entities across all modules", ArtifactConvention.Entities);

    private static Command Create(
        IFileSystem fileSystem,
        IConsoleOutput console,
        string name,
        string description,
        ArtifactConvention convention)
    {
        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit results as JSON instead of a table.",
        };

        var command = new Command(name, description)
        {
            solutionOption,
            jsonOption,
        };

        command.SetAction(parseResult =>
        {
            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new ListArtifactsHandler(fileSystem, console, solutionFinder);
            return handler.Execute(
                convention,
                parseResult.GetValue(solutionOption),
                parseResult.GetValue(jsonOption));
        });

        return command;
    }
}
