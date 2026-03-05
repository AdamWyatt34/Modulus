using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Modulus.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class PublicSetterOnEntityCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("MOD005");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var setAccessor = root.FindToken(diagnosticSpan.Start).Parent as AccessorDeclarationSyntax;
        if (setAccessor is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Change to private set",
                createChangedDocument: ct => MakeSetterPrivateAsync(context.Document, setAccessor, ct),
                equivalenceKey: "MOD005_PrivateSet"),
            diagnostic);
    }

    private static async Task<Document> MakeSetterPrivateAsync(
        Document document,
        AccessorDeclarationSyntax setAccessor,
        CancellationToken cancellationToken)
    {
        var privateModifier = SyntaxFactory.Token(SyntaxKind.PrivateKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);
        var newSetAccessor = setAccessor.WithModifiers(
            SyntaxFactory.TokenList(privateModifier));

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(setAccessor, newSetAccessor);

        return document.WithSyntaxRoot(newRoot);
    }
}