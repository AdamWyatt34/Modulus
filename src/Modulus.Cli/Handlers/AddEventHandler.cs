using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;
using Modulus.Templates;

namespace Modulus.Cli.Handlers;

public sealed class AddEventHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    public Task<int> ExecuteAsync(
        string eventName,
        string moduleName,
        string? solutionPath,
        string? properties)
    {
        if (!CSharpIdentifierValidator.IsValid(eventName))
        {
            console.WriteError($"'{eventName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return Task.FromResult(1);
        }

        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier.");
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

        var integrationDir = Path.Combine(moduleDir, "src", $"{moduleName}.Integration");
        if (!fileSystem.DirectoryExists(integrationDir))
        {
            console.WriteError($"The '{moduleName}.Integration' project was not found at '{integrationDir}'.");
            return Task.FromResult(1);
        }

        var eventFilePath = Path.Combine(integrationDir, "IntegrationEvents", $"{eventName}.cs");
        if (fileSystem.FileExists(eventFilePath))
        {
            console.WriteError($"Integration event '{eventName}' already exists at '{eventFilePath}'.");
            return Task.FromResult(1);
        }

        var generator = new EventGenerator();
        var outputs = generator.Generate(new EventOptions
        {
            EventName = eventName,
            ModuleName = moduleName,
            SolutionName = solutionName,
            Properties = parsedProperties,
        });

        var moduleRoot = Path.Combine("src", "Modules", moduleName);
        var fileCount = 0;

        foreach (var output in outputs)
        {
            var remappedPath = Path.Combine(moduleRoot, output.RelativePath);
            var fullPath = PathGuard.EnsureContained(solutionRoot, remappedPath);
            var dir = fileSystem.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException($"Could not determine directory for path: {fullPath}");
            fileSystem.CreateDirectory(dir);
            fileSystem.WriteAllText(fullPath, output.Content);
            fileCount++;
        }

        console.WriteSuccess($"Integration event '{eventName}' added to module '{moduleName}'.");
        console.WriteLine($"  Files created: {fileCount}");

        if (parsedProperties.Count > 0)
        {
            console.WriteLine($"  Properties: {string.Join(", ", parsedProperties.Select(p => $"{p.Name}:{p.Type}"))}");
        }

        console.WriteLine("");
        console.WriteLine("  Next steps:");
        console.WriteLine($"    1. Publish it from a handler via IMessageBus.Publish(new {eventName}(...))");
        console.WriteLine($"    2. Add a consumer in another module: modulus add-consumer {eventName} --module <ModuleName>");

        return Task.FromResult(0);
    }
}
