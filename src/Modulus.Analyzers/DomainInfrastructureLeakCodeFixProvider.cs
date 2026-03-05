using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Modulus.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class DomainInfrastructureLeakCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("MOD004");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        if (node is UsingDirectiveSyntax)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Remove using directive",
                    createChangedDocument: ct => RemoveNodeAsync(context.Document, node, ct),
                    equivalenceKey: "MOD004_RemoveUsing"),
                diagnostic);
        }
        else
        {
            var attribute = node.FirstAncestorOrSelf<AttributeSyntax>();
            if (attribute is null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Remove infrastructure attribute",
                    createChangedDocument: ct => RemoveAttributeAsync(context.Document, attribute, ct),
                    equivalenceKey: "MOD004_RemoveAttribute"),
                diagnostic);
        }
    }

    private static async Task<Document> RemoveNodeAsync(
        Document document,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var newRoot = root!.RemoveNode(node, SyntaxRemoveOptions.KeepExteriorTrivia);
        return document.WithSyntaxRoot(newRoot!);
    }

    private static async Task<Document> RemoveAttributeAsync(
        Document document,
        AttributeSyntax attribute,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        var attributeList = attribute.Parent as AttributeListSyntax;
        if (attributeList is null)
            return document;

        SyntaxNode newRoot;
        if (attributeList.Attributes.Count == 1)
        {
            // Only attribute in the list — remove the entire attribute list
            newRoot = root!.RemoveNode(attributeList, SyntaxRemoveOptions.KeepNoTrivia)!;
        }
        else
        {
            // Multiple attributes — remove just this one
            newRoot = root!.RemoveNode(attribute, SyntaxRemoveOptions.KeepNoTrivia)!;
        }

        return document.WithSyntaxRoot(newRoot);
    }
}