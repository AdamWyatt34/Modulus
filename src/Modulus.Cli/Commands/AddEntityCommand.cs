using System.CommandLine;
using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class AddEntityCommand
{
    public static Command Create(IFileSystem fileSystem, IConsoleOutput console)
    {
        var entityNameArg = new Argument<string>("entity-name")
        {
            Description = "PascalCase name of the entity to add",
        };

        var moduleOption = new Option<string>("--module")
        {
            Description = "Name of the target module",
            Required = true,
        };
        moduleOption.Aliases.Add("-m");

        var solutionOption = new Option<string?>("--solution")
        {
            Description = "Path to the solution file (default: auto-find in current or parent directories)",
        };
        solutionOption.Aliases.Add("-s");

        var aggregateOption = new Option<bool>("--aggregate")
        {
            Description = "Generate as AggregateRoot instead of Entity",
        };

        var idTypeOption = new Option<string>("--id-type")
        {
            Description = "ID type: guid (default), int, long, string, or a custom StronglyTypedId name",
        };
        idTypeOption.DefaultValueFactory = _ => "guid";

        var propertiesOption = new Option<string?>("--properties")
        {
            Description = "Comma-separated properties in Name:Type format (e.g. \"Name:string,Email:string,IsActive:bool\")",
        };
        propertiesOption.Aliases.Add("-p");

        var command = new Command("add-entity", "Add a new entity to an existing module")
        {
            entityNameArg,
            moduleOption,
            solutionOption,
            aggregateOption,
            idTypeOption,
            propertiesOption,
        };

        command.SetAction(async parseResult =>
        {
            var entityName = parseResult.GetValue(entityNameArg)!;
            var moduleName = parseResult.GetValue(moduleOption)!;
            var solution = parseResult.GetValue(solutionOption);
            var aggregate = parseResult.GetValue(aggregateOption);
            var idType = parseResult.GetValue(idTypeOption)!;
            var properties = parseResult.GetValue(propertiesOption);

            var solutionFinder = new SolutionFinder(fileSystem);
            var handler = new AddEntityHandler(fileSystem, console, solutionFinder);
            return await handler.ExecuteAsync(entityName, moduleName, solution, aggregate, idType, properties);
        });

        return command;
    }
}
