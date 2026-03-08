using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;
using Modulus.Templates;

namespace Modulus.Cli.Handlers;

public sealed class AddCommandHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public Task<int> ExecuteAsync(
        string commandName,
        string moduleName,
        string? solutionPath,
        string? resultType)
    {
        if (!CSharpIdentifierValidator.IsValid(commandName))
        {
            console.WriteError($"'{commandName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return Task.FromResult(1);
        }

        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier.");
            return Task.FromResult(1);
        }

        if (resultType is not null && !CSharpIdentifierValidator.IsValid(resultType))
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

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))
            ?? throw new InvalidOperationException($"Could not determine directory for path: {slnxPath}");
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

        var commandFilePath = Path.Combine(moduleDir, "src", $"{moduleName}.Application", "Commands", commandName, $"{commandName}.cs");
        if (fileSystem.FileExists(commandFilePath))
        {
            console.WriteError($"Command '{commandName}' already exists at '{commandFilePath}'.");
            return Task.FromResult(1);
        }

        var generator = new CommandGenerator();
        var outputs = generator.Generate(new CommandOptions
        {
            CommandName = commandName,
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
            var dir = fileSystem.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException($"Could not determine directory for path: {fullPath}");
            fileSystem.CreateDirectory(dir);
            fileSystem.WriteAllText(fullPath, output.Content);
            fileCount++;
        }

        var returnInfo = resultType is null ? "Result (void)" : $"Result<{resultType}>";
        console.WriteSuccess($"Command '{commandName}' added to module '{moduleName}'.");
        console.WriteLine($"  Files created: {fileCount}");
        console.WriteLine($"  Returns: {returnInfo}");
        console.WriteLine("");
        console.WriteLine("  Next steps:");
        console.WriteLine($"    1. Add properties to the {commandName} record");
        console.WriteLine($"    2. Add validation rules to {commandName}Validator");
        console.WriteLine($"    3. Implement the handler logic in {commandName}Handler");

        return Task.FromResult(0);
    }
}
