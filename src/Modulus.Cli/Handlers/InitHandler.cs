using System.Text;
using System.Text.Json;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Validation;
using Modulus.Templates;

namespace Modulus.Cli.Handlers;

public sealed class InitHandler(
    IFileSystem fileSystem,
    IProcessRunner processRunner,
    IConsoleOutput console)
{
    public async Task<int> ExecuteAsync(
        string solutionName,
        string outputDirectory,
        bool includeAspire,
        string transport,
        bool noGit)
    {
        if (!CSharpIdentifierValidator.IsValid(solutionName))
        {
            console.WriteError($"'{solutionName}' is not a valid C# identifier. Use PascalCase with letters, digits, and underscores.");
            return 1;
        }

        var solutionRoot = Path.Combine(outputDirectory, solutionName);

        if (fileSystem.DirectoryExists(solutionRoot))
        {
            var existing = fileSystem.GetFiles(solutionRoot, "*", SearchOption.TopDirectoryOnly);
            var existingDirs = fileSystem.GetDirectories(solutionRoot);
            if (existing.Count > 0 || existingDirs.Count > 0)
            {
                console.WriteError($"Directory '{solutionRoot}' already exists and is not empty.");
                return 1;
            }
        }

        var engine = new TemplateEngine();
        var outputs = engine.GenerateInit(new InitOptions
        {
            SolutionName = solutionName,
            IncludeAspire = includeAspire,
        });

        var fileCount = 0;
        foreach (var output in outputs)
        {
            var content = output.Content;

            if (output.RelativePath.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase))
            {
                content = InjectMessagingConfig(content, transport);
            }

            var fullPath = Path.Combine(solutionRoot, output.RelativePath);
            var dir = fileSystem.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException($"Could not determine directory for path: {fullPath}");
            fileSystem.CreateDirectory(dir);
            fileSystem.WriteAllText(fullPath, content);
            fileCount++;
        }

        console.WriteLine($"Created solution '{solutionName}' with {fileCount} files.");

        var restoreResult = await processRunner.RunAsync("dotnet", "restore", solutionRoot);
        if (restoreResult != 0)
        {
            console.WriteError($"Warning: dotnet restore failed with exit code {restoreResult}. You may need to run it manually.");
        }

        if (!noGit)
        {
            var gitResult = await processRunner.RunAsync("git", "init", solutionRoot);
            if (gitResult != 0)
            {
                console.WriteError("Warning: git init failed. You may need to initialize the repository manually.");
            }
            else
            {
                await processRunner.RunAsync("git", "add .", solutionRoot);
                await processRunner.RunAsync("git", "commit -m \"Initial commit from Modulus\"", solutionRoot);
            }
        }

        console.WriteSuccess($"Solution '{solutionName}' created successfully at {solutionRoot}");
        console.WriteLine($"  Aspire: {(includeAspire ? "Yes" : "No")}");
        console.WriteLine($"  Transport: {transport}");
        console.WriteLine($"  Git: {(noGit ? "Skipped" : "Initialized")}");

        return 0;
    }

    internal static string InjectMessagingConfig(string appSettingsContent, string transport)
    {
        using var doc = JsonDocument.Parse(appSettingsContent);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            property.WriteTo(writer);
        }

        writer.WritePropertyName("Messaging");
        writer.WriteStartObject();
        writer.WriteString("Transport", transport switch
        {
            "rabbitmq" => "RabbitMq",
            "azureservicebus" => "AzureServiceBus",
            _ => "InMemory",
        });

        if (transport is "rabbitmq" or "azureservicebus")
        {
            writer.WriteString("ConnectionString", "");
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
