using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;
using Modulus.Templates;

namespace Modulus.Cli.Handlers;

public sealed class AddEndpointHandler(
    IFileSystem fileSystem,
    IConsoleOutput console,
    SolutionFinder solutionFinder)
{
    private static readonly HashSet<string> ValidMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE",
    };

    public Task<int> ExecuteAsync(
        string endpointName,
        string moduleName,
        string? solutionPath,
        string method,
        string route,
        string? commandName,
        string? queryName,
        string? resultType)
    {
        if (!CSharpIdentifierValidator.IsValid(endpointName))
        {
            console.WriteError($"'{endpointName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return Task.FromResult(1);
        }

        if (!CSharpIdentifierValidator.IsValid(moduleName))
        {
            console.WriteError($"'{moduleName}' is not a valid C# identifier.");
            return Task.FromResult(1);
        }

        if (!ValidMethods.Contains(method))
        {
            console.WriteError($"'{method}' is not a supported HTTP method. Use GET, POST, PUT, or DELETE.");
            return Task.FromResult(1);
        }

        if (commandName is not null && queryName is not null)
        {
            console.WriteError("Options --command and --query are mutually exclusive. Specify only one.");
            return Task.FromResult(1);
        }

        if (queryName is not null && resultType is null)
        {
            console.WriteError("Option --result-type is required when using --query.");
            return Task.FromResult(1);
        }

        var slnxPath = solutionFinder.ResolveSolutionPath(solutionPath, fileSystem.GetCurrentDirectory());
        if (slnxPath is null)
        {
            console.WriteError("Could not find a solution file. Use --solution to specify the path, or run from within a Modulus solution directory.");
            return Task.FromResult(1);
        }

        var solutionRoot = fileSystem.GetDirectoryName(fileSystem.GetFullPath(slnxPath))!;
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

        var endpointsDir = Path.Combine(moduleDir, "src", $"{moduleName}.Api", "Endpoints");
        var endpointFilePath = Path.Combine(endpointsDir, $"{endpointName}.cs");

        if (fileSystem.FileExists(endpointFilePath))
        {
            console.WriteError($"An endpoint file '{endpointName}.cs' already exists at '{endpointFilePath}'.");
            return Task.FromResult(1);
        }

        var generator = new EndpointGenerator();
        var output = generator.Generate(new EndpointOptions
        {
            EndpointName = endpointName,
            ModuleName = moduleName,
            SolutionName = solutionName,
            HttpMethod = method.ToUpperInvariant(),
            Route = route,
            CommandName = commandName,
            QueryName = queryName,
            ResultType = resultType,
        });

        fileSystem.WriteAllText(endpointFilePath, output.Content);

        console.WriteSuccess($"Endpoint '{endpointName}' added to {moduleName} at Endpoints/{endpointName}.cs.");
        console.WriteLine($"  Method: {method.ToUpperInvariant()}");
        console.WriteLine($"  Route: /api/{moduleName.ToLowerInvariant()}{route}");
        if (commandName is not null) console.WriteLine($"  Wired to command: {commandName}");
        if (queryName is not null) console.WriteLine($"  Wired to query: {queryName}");

        return Task.FromResult(0);
    }
}
