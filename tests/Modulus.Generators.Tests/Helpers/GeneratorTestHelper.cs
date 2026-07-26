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

    public static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult) RunGenerator(
        string source, bool includeEfCoreReference = true, string? rootNamespace = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Excluding the EF Core reference proves the generator's ValueConverter gate actually
        // works — the default (true) reflects every other test in this file, since
        // Modulus.Generators.Tests.csproj itself references EF Core, so TRUSTED_PLATFORM_ASSEMBLIES
        // always includes it unless explicitly filtered out here.
        var references = includeEfCoreReference
            ? LazyReferences.Value
            : LazyReferences.Value
                .Where(r => r.Display is null || !r.Display.Contains("Microsoft.EntityFrameworkCore"))
                .ToList();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new StronglyTypedIdGenerator();

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

    /// <summary>
    /// Compiles <paramref name="referencedSource"/> as its own assembly — running
    /// <see cref="StronglyTypedIdGenerator"/> on it first, so the emitted metadata genuinely
    /// carries (or omits) the nested <c>{Name}ValueConverter</c> depending on
    /// <paramref name="referencedIncludeEfCoreReference"/> — then references it while running the
    /// generator on <paramref name="hostSource"/>. Used to prove the bulk EF Core registration
    /// helper's referenced-assembly scan checks real compiled metadata instead of assuming a
    /// converter exists just because the type carries <c>[StronglyTypedId]</c>.
    /// </summary>
    public static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult) RunGeneratorWithReferencedAssembly(
        string hostSource,
        string referencedSource,
        bool hostIncludeEfCoreReference = true,
        bool referencedIncludeEfCoreReference = true,
        string? rootNamespace = null)
    {
        var referencedReferences = referencedIncludeEfCoreReference
            ? LazyReferences.Value
            : LazyReferences.Value
                .Where(r => r.Display is null || !r.Display.Contains("Microsoft.EntityFrameworkCore"))
                .ToList();

        var referencedSyntaxTree = CSharpSyntaxTree.ParseText(referencedSource);
        var referencedCompilation = CSharpCompilation.Create(
            "ReferencedIdAssembly",
            [referencedSyntaxTree],
            referencedReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var referencedGenerator = new StronglyTypedIdGenerator();
        GeneratorDriver referencedDriver = CSharpGeneratorDriver.Create(referencedGenerator);
        referencedDriver = referencedDriver.RunGeneratorsAndUpdateCompilation(
            referencedCompilation, out var referencedOutputCompilation, out _);

        using var ms = new MemoryStream();
        var emitResult = referencedOutputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = string.Join(", ", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage()));
            throw new InvalidOperationException($"Referenced ID assembly failed to compile: {errors}");
        }

        ms.Seek(0, SeekOrigin.Begin);

        var hostReferences = new List<MetadataReference>(
            hostIncludeEfCoreReference
                ? LazyReferences.Value
                : LazyReferences.Value.Where(r => r.Display is null || !r.Display.Contains("Microsoft.EntityFrameworkCore")))
        {
            MetadataReference.CreateFromStream(ms)
        };

        var hostSyntaxTree = CSharpSyntaxTree.ParseText(hostSource);
        var hostCompilation = CSharpCompilation.Create(
            "HostAssembly",
            [hostSyntaxTree],
            hostReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var hostGenerator = new StronglyTypedIdGenerator();

        AnalyzerConfigOptionsProvider? optionsProvider = rootNamespace is not null
            ? new TestAnalyzerConfigOptionsProvider(rootNamespace)
            : null;

        GeneratorDriver hostDriver = CSharpGeneratorDriver.Create(
            generators: [hostGenerator.AsSourceGenerator()],
            optionsProvider: optionsProvider);

        hostDriver = hostDriver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out var diagnostics);
        var runResult = hostDriver.GetRunResult();

        return (outputCompilation, diagnostics, runResult);
    }

    /// <summary>
    /// Emits <paramref name="compilation"/> to an in-memory assembly and loads it, so a generated
    /// strongly typed ID can actually be constructed, parsed, compared, and (de)serialized via
    /// reflection at test time instead of only asserting on the generated source text.
    /// </summary>
    public static System.Reflection.Assembly EmitToAssembly(Compilation compilation)
    {
        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = string.Join(", ", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage()));
            throw new InvalidOperationException($"Compilation failed to emit: {errors}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        return System.Reflection.Assembly.Load(ms.ToArray());
    }

    public static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult) RunHandlerRegistrationGenerator(
        string source,
        string? rootNamespace = null,
        bool dependencyInjectionReferences = true)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // dependencyInjectionReferences: false models a Domain project that carries the generator
        // solely for [StronglyTypedId] and has no Microsoft.Extensions.DependencyInjection.
        var references = dependencyInjectionReferences
            ? (IReadOnlyList<MetadataReference>)LazyReferences.Value
            : LazyReferences.Value
                .Where(r => r.Display is null || !r.Display.Contains("Microsoft.Extensions.DependencyInjection"))
                .ToList();

        var compilation = CSharpCompilation.Create(
            rootNamespace ?? "TestAssembly",
            [syntaxTree],
            references,
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

    public static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult) RunHandlerRegistrationGenerator(
        string hostSource,
        string? rootNamespace,
        params string[] referencedAssemblySources)
    {
        var references = new List<MetadataReference>(LazyReferences.Value);

        for (var i = 0; i < referencedAssemblySources.Length; i++)
        {
            var moduleSyntaxTree = CSharpSyntaxTree.ParseText(referencedAssemblySources[i]);
            var moduleCompilation = CSharpCompilation.Create(
                $"ReferencedAssembly{i}",
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
                    $"Referenced assembly {i} failed to compile: {errors}");
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

        var generator = new HandlerRegistrationGenerator();

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

    public static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult)
        RunModuleRegistrationGenerator(
            string hostSource,
            string? rootNamespace = null,
            params string[] moduleAssemblySources)
        => RunModuleRegistrationGeneratorCore(hostSource, rootNamespace, true, moduleAssemblySources);

    public static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult)
        RunModuleRegistrationGenerator(
            string hostSource,
            string? rootNamespace,
            bool aspNetCoreReferences,
            params string[] moduleAssemblySources)
        => RunModuleRegistrationGeneratorCore(hostSource, rootNamespace, aspNetCoreReferences, moduleAssemblySources);

    private static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult)
        RunModuleRegistrationGeneratorCore(
            string hostSource,
            string? rootNamespace,
            bool aspNetCoreReferences,
            string[] moduleAssemblySources)
    {
        var references = aspNetCoreReferences
            ? new List<MetadataReference>(LazyReferences.Value)
            : new List<MetadataReference>(LazyReferences.Value.Where(r =>
                r.Display is null || !r.Display.Contains("Microsoft.AspNetCore")));

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
