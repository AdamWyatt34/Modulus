using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Modulus.Mediator.Abstractions;

namespace Modulus.Analyzers.Tests.Helpers;

internal static class AnalyzerTestHelper
{
    private static readonly Lazy<List<MetadataReference>> LazyReferences = new(BuildReferences);

    private static List<MetadataReference> BuildReferences()
    {
        var references = new List<MetadataReference>();

        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (trustedAssemblies is not null)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                if (File.Exists(path))
                    references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        AddAssemblyIfNotPresent<IMediator>(references);

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

    public static CSharpCompilation CreateCompilation(
        string source,
        string? assemblyName = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        return CSharpCompilation.Create(
            assemblyName ?? "TestAssembly",
            [syntaxTree],
            LazyReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static CSharpCompilation CreateCompilationWithReference(
        string source,
        string assemblyName,
        string referenceSource,
        string referenceAssemblyName)
    {
        var refTree = CSharpSyntaxTree.ParseText(referenceSource);
        var refCompilation = CSharpCompilation.Create(
            referenceAssemblyName,
            [refTree],
            LazyReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = refCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = string.Join(", ", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage()));
            throw new InvalidOperationException(
                $"Reference assembly '{referenceAssemblyName}' failed to compile: {errors}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        var refMetadata = MetadataReference.CreateFromStream(ms);

        var references = new List<MetadataReference>(LazyReferences.Value) { refMetadata };
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        DiagnosticAnalyzer analyzer,
        string source,
        string? assemblyName = null)
    {
        var compilation = CreateCompilation(source, assemblyName);
        return await GetDiagnosticsAsync(analyzer, compilation);
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        DiagnosticAnalyzer analyzer,
        CSharpCompilation compilation)
    {
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create(analyzer));

        var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        return allDiagnostics
            .Where(d => d.Id.StartsWith("MOD"))
            .ToImmutableArray();
    }

    public static async Task<string> ApplyCodeFixAsync(
        DiagnosticAnalyzer analyzer,
        CodeFixProvider codeFix,
        string source,
        string diagnosticId,
        string? assemblyName = null)
    {
        var compilation = CreateCompilation(source, assemblyName);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var diagnostic = diagnostics.First(d => d.Id == diagnosticId);

        var tree = compilation.SyntaxTrees.First();
        var (document, workspace) = CreateDocument(source, assemblyName);
        using (workspace)
        {
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) => actions.Add(action),
                CancellationToken.None);

            await codeFix.RegisterCodeFixesAsync(context);

            if (actions.Count == 0)
                throw new InvalidOperationException("No code fix actions registered.");

            var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
            var changedSolution = operations
                .OfType<ApplyChangesOperation>()
                .Single()
                .ChangedSolution;

            var changedDocument = changedSolution.GetDocument(document.Id)!;
            var changedText = await changedDocument.GetTextAsync();
            return changedText.ToString();
        }
    }

    private static (Microsoft.CodeAnalysis.Document Document, AdhocWorkspace Workspace) CreateDocument(
        string source,
        string? assemblyName = null)
    {
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution
            .AddProject(projectId, assemblyName ?? "TestAssembly", assemblyName ?? "TestAssembly", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(projectId, LazyReferences.Value)
            .AddDocument(documentId, "Test.cs", SourceText.From(source));

        return (solution.GetDocument(documentId)!, workspace);
    }
}
