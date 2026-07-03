using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class DoctorCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit results as a single JSON document instead of human-readable output.",
        };

        var strictOption = new Option<bool>("--strict")
        {
            Description = "Treat warnings as failures for exit-code purposes (exit code 2 when only warnings are present).",
        };

        var command = new Command("doctor", "Validates the health of a Modulus-scaffolded solution without building it.")
        {
            solutionOption,
            jsonOption,
            strictOption,
        };

        command.SetAction(async parseResult =>
        {
            var solutionPath = parseResult.GetValue(solutionOption);
            var json = parseResult.GetValue(jsonOption);
            var strict = parseResult.GetValue(strictOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new DoctorHandler(fileSystem, console, solutionFinder);
            return await handler.ExecuteAsync(solutionPath, json, strict);
        });

        return command;
    }
}
