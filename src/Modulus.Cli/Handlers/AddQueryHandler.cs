using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;
using Modulus.Templates;

namespace Modulus.Cli.Handlers;

public sealed class AddQueryHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public Task<int> ExecuteAsync(
        string queryName,
        string moduleName,
        string? solutionPath,
        string resultType)
    {
        if (!CSharpIdentifierValidator.IsValid(queryName))
        {
            console.WriteError($"'{queryName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return Task.FromResult(1);
        }

        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier.");
            return Task.FromResult(1);
        }

        if (!CSharpIdentifierValidator.IsValid(resultType))
        {
            console.WriteError($"'{resultType}' is not a valid C# type name.");
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

        var queryFilePath = Path.Combine(moduleDir, "src", $"{moduleName}.Application", "Queries", queryName, $"{queryName}.cs");
        if (fileSystem.FileExists(queryFilePath))
        {
            console.WriteError($"Query '{queryName}' already exists at '{queryFilePath}'.");
            return Task.FromResult(1);
        }

        var generator = new QueryGenerator();
        var outputs = generator.Generate(new QueryOptions
        {
            QueryName = queryName,
            ModuleName = moduleName,
            SolutionName = solutionName,
            ResultType = resultType,
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

        console.WriteSuccess($"Query '{queryName}' added to module '{moduleName}'.");
        console.WriteLine($"  Files created: {fileCount}");
        console.WriteLine($"  Returns: Result<{resultType}>");
        console.WriteLine("");
        console.WriteLine("  Next steps:");
        console.WriteLine($"    1. Add properties to the {queryName} record");
        console.WriteLine($"    2. Implement the handler logic in {queryName}Handler");

        return Task.FromResult(0);
    }
}
