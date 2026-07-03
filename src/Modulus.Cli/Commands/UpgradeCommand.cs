using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class UpgradeCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var versionOption = new Option<string?>("--version")
        {
            Description = "Target ModulusKit.* package version (default: the CLI's own version).",
        };

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Show the version changes without writing Directory.Packages.props.",
        };

        var command = new Command("upgrade", "Bumps all ModulusKit.* package pins in Directory.Packages.props to a target version.")
        {
            versionOption,
            solutionOption,
            dryRunOption,
        };

        command.SetAction(async parseResult =>
        {
            var version = parseResult.GetValue(versionOption);
            var solutionPath = parseResult.GetValue(solutionOption);
            var dryRun = parseResult.GetValue(dryRunOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new UpgradeHandler(fileSystem, console, solutionFinder);
            return await handler.ExecuteAsync(version, solutionPath, dryRun);
        });

        return command;
    }
}
