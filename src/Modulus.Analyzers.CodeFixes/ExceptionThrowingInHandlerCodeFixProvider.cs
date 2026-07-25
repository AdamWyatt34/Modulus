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

        // Only throw STATEMENTS get a safe fix here. Throw EXPRESSIONS (e.g. `x ?? throw new
        // Foo()`, which the analyzer also flags) sit in an expression position — substituting a
        // `return` statement there would produce an invalid syntax tree, so no fix is offered.
        var throwStatement = node as ThrowStatementSyntax ?? node.FirstAncestorOrSelf<ThrowStatementSyntax>();
        if (throwStatement?.Expression is not { } creationExpr)
            return;

        if (creationExpr is not ObjectCreationExpressionSyntax and not ImplicitObjectCreationExpressionSyntax)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

        string exceptionName;
        ExpressionSyntax? firstArgExpr;

        if (creationExpr is ObjectCreationExpressionSyntax objectCreation)
        {
            exceptionName = objectCreation.Type?.ToString() ?? "Exception";
            firstArgExpr = objectCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        }
        else
        {
            var implicitCreation = (ImplicitObjectCreationExpressionSyntax)creationExpr;
            var typeSymbol = semanticModel?.GetTypeInfo(creationExpr, context.CancellationToken).Type;
            exceptionName = typeSymbol?.Name ?? "Exception";
            firstArgExpr = implicitCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        }

        var description = DescribeArgument(firstArgExpr, semanticModel, context.CancellationToken, exceptionName);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Replace with Error.{errorMethod}() result",
                createChangedDocument: ct => ReplaceThrowWithReturnAsync(
                    context.Document, throwStatement, errorMethod, exceptionName, description, semanticModel, ct),
                equivalenceKey: $"MOD003_{errorMethod}"),
            diagnostic);
    }

    private static string DescribeArgument(
        ExpressionSyntax? argument,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken,
        string exceptionName)
    {
        if (argument is null)
            return $"\"{exceptionName}\"";

        // A string-typed argument (literal, interpolation, variable, nameof(...), ...) can be
        // embedded as-is; anything else is wrapped in an interpolation so the emitted
        // `Error.X("code", <description>)` call always binds to a string parameter instead of
        // producing a CS1503 for e.g. an int argument.
        var isString = semanticModel?.GetTypeInfo(argument, cancellationToken).Type?.SpecialType == SpecialType.System_String
            || argument.IsKind(SyntaxKind.StringLiteralExpression)
            || argument is InterpolatedStringExpressionSyntax;

        return isString ? argument.ToString() : "$\"{" + argument + "}\"";
    }

    private static async Task<Document> ReplaceThrowWithReturnAsync(
        Document document,
        ThrowStatementSyntax throwStatement,
        string errorMethod,
        string exceptionName,
        string description,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        var errorCall = $"Error.{errorMethod}(\"{exceptionName}\", {description})";
        var returnStatement = BuildReturnStatement(throwStatement, errorCall, semanticModel, cancellationToken)
            .WithLeadingTrivia(throwStatement.GetLeadingTrivia())
            .WithTrailingTrivia(throwStatement.GetTrailingTrivia());

        var newRoot = root!.ReplaceNode(throwStatement, returnStatement);
        return document.WithSyntaxRoot(newRoot);
    }

    private static ReturnStatementSyntax BuildReturnStatement(
        ThrowStatementSyntax throwStatement,
        string errorCall,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        var methodDeclaration = throwStatement.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        var isAsync = methodDeclaration?.Modifiers.Any(SyntaxKind.AsyncKeyword) ?? false;

        // Async methods (and the defensive fallback when no enclosing method is found) can return
        // the Result value directly — the compiler wraps it in the Task<...> the method declares.
        // A non-async handler must construct that Task itself: `return Error.X(...);` doesn't
        // convert to `Task<Result>` (CS0029).
        var resultTypeArgument = isAsync || methodDeclaration is null
            ? null
            : GetTaskResultTypeArgument(semanticModel, methodDeclaration, cancellationToken);

        var expressionText = resultTypeArgument is null
            ? errorCall
            : $"Task.FromResult<{resultTypeArgument}>({errorCall})";

        return SyntaxFactory.ReturnStatement(SyntaxFactory.ParseExpression(expressionText));
    }

    private static string? GetTaskResultTypeArgument(
        SemanticModel? semanticModel,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        if (semanticModel is null)
            return null;

        if (semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) is not IMethodSymbol
            {
                ReturnType: INamedTypeSymbol { Name: "Task", Arity: 1 } taskType
            })
        {
            return null;
        }

        // Minimally-qualified relative to the method's own position so the emitted
        // `Task.FromResult<...>` argument matches whatever spelling ("Result", "Result<int>", a
        // fully-qualified name, ...) the file already uses for its return type.
        return taskType.TypeArguments[0].ToMinimalDisplayString(semanticModel, methodDeclaration.ReturnType.SpanStart, null);
    }
}
