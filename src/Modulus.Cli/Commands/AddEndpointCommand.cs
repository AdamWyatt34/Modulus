using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class AddEndpointCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var endpointNameArg = new Argument<string>("endpoint-name")
        {
            Description = "PascalCase name for the endpoint (used in .WithName())",
        };

        var moduleOption = new Option<string>("--module")
        {
            Description = "Name of the target module",
            Required = true,
        };
        moduleOption.Aliases.Add("-m");

        var methodOption = new Option<string>("--method")
        {
            Description = "HTTP method: GET, POST, PUT, DELETE (default: GET)",
        };
        methodOption.DefaultValueFactory = _ => "GET";

        var routeOption = new Option<string>("--route")
        {
            Description = "Route template relative to the module group (default: /)",
        };
        routeOption.DefaultValueFactory = _ => "/";

        var commandOption = new Option<string?>("--command")
        {
            Description = "Name of the command to wire up (mutually exclusive with --query)",
        };

        var queryOption = new Option<string?>("--query")
        {
            Description = "Name of the query to wire up (mutually exclusive with --command)",
        };

        var resultTypeOption = new Option<string?>("--result-type")
        {
            Description = "The result type T when wiring a query or typed command",
        };
        resultTypeOption.Aliases.Add("-r");

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find)",
        };
        solutionOption.Aliases.Add("-s");

        var command = new Command("add-endpoint", "Add a new endpoint to a module's endpoint file")
        {
            endpointNameArg,
            moduleOption,
            methodOption,
            routeOption,
            commandOption,
            queryOption,
            resultTypeOption,
            solutionOption,
        };

        command.SetAction(async parseResult =>
        {
            var endpointName = parseResult.GetValue(endpointNameArg)!;
            var moduleName = parseResult.GetValue(moduleOption)!;
            var method = parseResult.GetValue(methodOption)!;
            var route = parseResult.GetValue(routeOption)!;
            var cmdName = parseResult.GetValue(commandOption);
            var qryName = parseResult.GetValue(queryOption);
            var resultType = parseResult.GetValue(resultTypeOption);
            var solution = parseResult.GetValue(solutionOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new AddEndpointHandler(fileSystem, console, solutionFinder);
            return await handler.ExecuteAsync(endpointName, moduleName, solution, method, route, cmdName, qryName, resultType);
        });

        return command;
    }
}
