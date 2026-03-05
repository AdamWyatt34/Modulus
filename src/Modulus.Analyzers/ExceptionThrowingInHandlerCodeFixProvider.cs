using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Modulus.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class ExceptionThrowingInHandlerCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("MOD003");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // Extract the error method name from diagnostic properties
        if (!diagnostic.Properties.TryGetValue("ErrorMethod", out var errorMethod) || errorMethod is null)
            return;

        // Find the throw statement or expression
        var throwNode = node is ThrowStatementSyntax || node is ThrowExpressionSyntax
            ? node
            : node.FirstAncestorOrSelf<ThrowStatementSyntax>() as SyntaxNode
                ?? node.FirstAncestorOrSelf<ThrowExpressionSyntax>();

        if (throwNode is null)
            return;

        // Extract the exception creation expression
        ExpressionSyntax? creationExpr = throwNode switch
        {
            ThrowStatementSyntax ts => ts.Expression,
            ThrowExpressionSyntax te => te.Expression,
            _ => null
        };

        string exceptionName;
        ExpressionSyntax? firstArgExpr;

        if (creationExpr is ObjectCreationExpressionSyntax objectCreation)
        {
            exceptionName = objectCreation.Type?.ToString() ?? "Exception";
            firstArgExpr = objectCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        }
        else if (creationExpr is ImplicitObjectCreationExpressionSyntax implicitCreation)
        {
            var typeInfo = context.Document.GetSemanticModelAsync(context.CancellationToken).Result;
            var typeSymbol = typeInfo?.GetTypeInfo(creationExpr).Type;
            exceptionName = typeSymbol?.Name ?? "Exception";
            firstArgExpr = implicitCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        }
        else
        {
            return;
        }

        var description = firstArgExpr?.ToString() ?? $"\"{exceptionName}\"";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Replace with Error.{errorMethod}() result",
                createChangedDocument: ct => ReplaceThrowWithReturnAsync(
                    context.Document, throwNode, errorMethod, exceptionName, description, ct),
                equivalenceKey: $"MOD003_{errorMethod}"),
            diagnostic);
    }

    private static async Task<Document> ReplaceThrowWithReturnAsync(
        Document document,
        SyntaxNode throwNode,
        string errorMethod,
        string exceptionName,
        string description,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        // Build: return Error.{errorMethod}("{exceptionName}", {description})
        var errorCall = SyntaxFactory.ParseExpression(
            $"Error.{errorMethod}(\"{exceptionName}\", {description})");

        var returnStatement = SyntaxFactory.ReturnStatement(errorCall)
            .WithLeadingTrivia(throwNode.GetLeadingTrivia())
            .WithTrailingTrivia(throwNode.GetTrailingTrivia());

        var newRoot = root!.ReplaceNode(throwNode, returnStatement);
        return document.WithSyntaxRoot(newRoot);
    }
}