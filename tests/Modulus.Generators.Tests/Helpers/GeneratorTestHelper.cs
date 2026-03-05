using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Modulus.Mediator.Abstractions;

namespace Modulus.Generators.Tests.Helpers;

internal static class GeneratorTestHelper
{
    private static readonly Lazy<List<MetadataReference>> LazyReferences = new(BuildReferences);

    private static List<MetadataReference> BuildReferences()
    {
        var references = new List<MetadataReference>();

        // Get all trusted platform assemblies (the .NET runtime assemblies)
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (trustedAssemblies is not null)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                if (File.Exists(path))
                    references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        // Add project assemblies that might not be in trusted assemblies
        AddAssemblyIfNotPresent<StronglyTypedIdAttribute>(references);
        AddAssemblyIfNotPresent<Modulus.Messaging.Abstractions.IIntegrationEvent>(references);
        AddAssemblyIfNotPresent<FluentValidation.IValidator>(references);

        return references;
    }

    private static void AddAssemblyIfNotPresent<T>(List<MetadataReference> references)
    {
        var location = typeof(T).Assembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
            return;

        if (!references.Any(r => string.Equals(r.Display, location, StringComparison.OrdinalIgnoreCase)))
            references.Add(MetadataReference.CreateFromFile(location));
    }

    public static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            LazyReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new StronglyTypedIdGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        var runResult = driver.GetRunResult();

        return (outputCompilation, diagnostics, runResult);
    }

    public static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult) RunHandlerRegistrationGenerator(
        string source,
        string? rootNamespace = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            rootNamespace ?? "TestAssembly",
            [syntaxTree],
            LazyReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HandlerRegistrationGenerator();

        AnalyzerConfigOptionsProvider? optionsProvider = rootNamespace is not null
            ? new TestAnalyzerConfigOptionsProvider(rootNamespace)
            : null;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            optionsProvider: optionsProvider);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        var runResult = driver.GetRunResult();

        return (outputCompilation, diagnostics, runResult);
    }

    public static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult)
        RunModuleRegistrationGenerator(
            string hostSource,
            string? rootNamespace = null,
            params string[] moduleAssemblySources)
    {
        var references = new List<MetadataReference>(LazyReferences.Value);

        for (var i = 0; i < moduleAssemblySources.Length; i++)
        {
            var moduleSyntaxTree = CSharpSyntaxTree.ParseText(moduleAssemblySources[i]);
            var moduleCompilation = CSharpCompilation.Create(
                $"ModuleAssembly{i}",
                [moduleSyntaxTree],
                LazyReferences.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var ms = new MemoryStream();
            var emitResult = moduleCompilation.Emit(ms);
            if (!emitResult.Success)
            {
                var errors = string.Join(", ", emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage()));
                throw new InvalidOperationException(
                    $"Module assembly {i} failed to compile: {errors}");
            }

            ms.Seek(0, SeekOrigin.Begin);
            references.Add(MetadataReference.CreateFromStream(ms));
        }

        var hostSyntaxTree = CSharpSyntaxTree.ParseText(hostSource);
        var hostCompilation = CSharpCompilation.Create(
            rootNamespace ?? "TestHost",
            [hostSyntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ModuleRegistrationGenerator();

        AnalyzerConfigOptionsProvider? optionsProvider = rootNamespace is not null
            ? new TestAnalyzerConfigOptionsProvider(rootNamespace)
            : null;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            optionsProvider: optionsProvider);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            hostCompilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        return (outputCompilation, diagnostics, runResult);
    }

    public static string GetGeneratedSource(GeneratorDriverRunResult runResult, string hintName)
    {
        return runResult.GeneratedTrees
            .Single(t => t.FilePath.EndsWith(hintName))
            .GetText()
            .ToString();
    }
}
