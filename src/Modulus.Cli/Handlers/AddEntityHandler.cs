using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;
using Modulus.Templates;

namespace Modulus.Cli.Handlers;

public sealed class AddEntityHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public Task<int> ExecuteAsync(
        string entityName,
        string moduleName,
        string? solutionPath,
        bool isAggregate,
        string idType,
        string? properties)
    {
        if (!CSharpIdentifierValidator.IsValid(entityName))
        {
            console.WriteError($"'{entityName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return Task.FromResult(1);
        }

        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier.");
            return Task.FromResult(1);
        }

        var resolvedIdType = ResolveIdType(idType);

        if (IsCustomStronglyTypedId(idType) && !CSharpIdentifierValidator.IsValid(resolvedIdType))
        {
            console.WriteError($"'{idType}' is not a valid C# identifier for an ID type.");
            return Task.FromResult(1);
        }

        var (parsedProperties, parseError) = PropertyParser.Parse(properties);
        if (parseError is not null)
        {
            console.WriteError(parseError);
            return Task.FromResult(1);
        }

        var slnxPath = solutionFinder.ResolveSolutionPath(solutionPath, fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError("Could not find a solution file. Use --solution to specify the path, or run from within a Modulus solution directory.");
            return Task.FromResult(1);
        }

        var solutionRoot = Path.GetDirectoryName(Path.GetFullPath(slnxPath))!;
        var solutionName = SolutionFinder.GetSolutionName(slnxPath);

        if (!solutionFinder.IsModulusSolution(solutionRoot, solutionName))
        {
            console.WriteError($"The solution at '{solutionRoot}' does not appear to be a Modulus solution.");
            return Task.FromResult(1);
        }

        var moduleDir = Path.Combine(solutionRoot, "src", "Modules", moduleName);
        if (!fileSystem.DirectoryExists(moduleDir))
        {
            console.WriteError($"Module '{moduleName}' was not found at '{moduleDir}'. Run 'modulus add-module {moduleName}' first.");
            return Task.FromResult(1);
        }

        var entityFilePath = Path.Combine(moduleDir, "src", $"{moduleName}.Domain", "Entities", $"{entityName}.cs");
        if (fileSystem.FileExists(entityFilePath))
        {
            console.WriteError($"Entity '{entityName}' already exists at '{entityFilePath}'.");
            return Task.FromResult(1);
        }

        var generator = new EntityGenerator();
        var outputs = generator.Generate(new EntityOptions
        {
            EntityName = entityName,
            ModuleName = moduleName,
            SolutionName = solutionName,
            IsAggregate = isAggregate,
            IdType = resolvedIdType,
            Properties = parsedProperties,
        });

        var moduleRoot = Path.Combine("src", "Modules", moduleName);
        var fileCount = 0;

        foreach (var output in outputs)
        {
            var remappedPath = Path.Combine(moduleRoot, output.RelativePath);
            var fullPath = Path.Combine(solutionRoot, remappedPath);
            var dir = Path.GetDirectoryName(fullPath)!;
            fileSystem.CreateDirectory(dir);
            fileSystem.WriteAllText(fullPath, output.Content);
            fileCount++;
        }

        var entityType = isAggregate ? "aggregate root" : "entity";
        console.WriteSuccess($"Entity '{entityName}' added to module '{moduleName}' as {entityType}.");
        console.WriteLine($"  Files created: {fileCount}");
        console.WriteLine($"  ID type: {resolvedIdType}");

        if (parsedProperties.Count > 0)
        {
            console.WriteLine($"  Properties: {string.Join(", ", parsedProperties.Select(p => $"{p.Name}:{p.Type}"))}");
        }

        console.WriteLine("");
        console.WriteLine("  Next steps:");
        console.WriteLine($"    1. Register I{entityName}Repository in your module's DI container ({moduleName}Module.cs)");
        console.WriteLine($"    2. Add a DbSet<{entityName}> to {moduleName}DbContext (optional, configs are auto-discovered)");

        return Task.FromResult(0);
    }

    private static readonly HashSet<string> BuiltInIdTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "guid", "int", "long", "string",
    };

    private static bool IsCustomStronglyTypedId(string idType) =>
        !BuiltInIdTypes.Contains(idType);

    private static string ResolveIdType(string idType) => idType.ToLowerInvariant() switch
    {
        "guid" => "Guid",
        "int" => "int",
        "long" => "long",
        "string" => "string",
        _ => idType,
    };
}
