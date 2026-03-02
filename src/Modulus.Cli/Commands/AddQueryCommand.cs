using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class AddQueryCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var queryNameArg = new Argument<string>("query-name")
        {
            Description = "PascalCase name of the query to add (e.g. GetProductById)",
        };

        var moduleOption = new Option<string>("--module")
        {
            Description = "Name of the target module",
            Required = true,
        };
        moduleOption.Aliases.Add("-m");

        var resultTypeOption = new Option<string>("--result-type")
        {
            Description = "The result type T in Result<T> (required for queries)",
            Required = true,
        };
        resultTypeOption.Aliases.Add("-r");

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var command = new Command("add-query", "Add a new query with handler to an existing module")
        {
            queryNameArg,
            moduleOption,
            resultTypeOption,
            solutionOption,
        };

        command.SetAction(async parseResult =>
        {
            var queryName = parseResult.GetValue(queryNameArg)!;
            var moduleName = parseResult.GetValue(moduleOption)!;
            var resultType = parseResult.GetValue(resultTypeOption)!;
            var solution = parseResult.GetValue(solutionOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new AddQueryHandler(fileSystem, console, solutionFinder);
            return await handler.ExecuteAsync(queryName, moduleName, solution, resultType);
        });

        return command;
    }
}
