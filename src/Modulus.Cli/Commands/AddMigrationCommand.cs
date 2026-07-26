using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class AddMigrationCommand
{
    public static Command Create(IFileSystem fileSystem, IProcessRunner processRunner, IConsoleOutput console)
    {
        var migrationNameArg = new Argument<string>("migration-name")
        {
            Description = "PascalCase name of the migration (e.g. AddOrderTable)",
        };

        var moduleOption = new Option<string>("--module")
        {
            Description = "The module whose DbContext the migration targets",
            Required = true,
        };
        moduleOption.Aliases.Add("-m");

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var contextOption = new Option<string?>("--context")
        {
            Description = "DbContext class name (default: {Module}DbContext). The read-only context never gets migrations.",
        };

        var outputDirOption = new Option<string>("--output-dir")
        {
            Description = "Migrations directory relative to the module's Infrastructure project (default: Migrations)",
            DefaultValueFactory = _ => "Migrations",
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Print the exact 'dotnet ef' invocation without running it.",
        };

        var command = new Command(
            "add-migration",
            "Add an EF Core migration for a module's DbContext (wraps 'dotnet ef migrations add' with the solution's project layout)")
        {
            migrationNameArg,
            moduleOption,
            solutionOption,
            contextOption,
            outputDirOption,
            dryRunOption,
        };

        command.SetAction(async parseResult =>
        {
            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new AddMigrationHandler(fileSystem, processRunner, console, solutionFinder);
            return await handler.ExecuteAsync(
                parseResult.GetValue(migrationNameArg)!,
                parseResult.GetValue(moduleOption)!,
                parseResult.GetValue(solutionOption),
                parseResult.GetValue(contextOption),
                parseResult.GetValue(outputDirOption)!,
                parseResult.GetValue(dryRunOption));
        });

        return command;
    }
}
