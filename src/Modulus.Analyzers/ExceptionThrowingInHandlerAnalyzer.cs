using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Modulus.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExceptionThrowingInHandlerAnalyzer : DiagnosticAnalyzer
{
    private const string MediatorNamespace = "Modulus.Mediator.Abstractions";

    private static readonly ImmutableHashSet<string> HandlerMetadataNames = ImmutableHashSet.Create(
        "ICommandHandler`1",
        "ICommandHandler`2",
        "IQueryHandler`2");

    private static readonly ImmutableHashSet<string> ExcludedExceptions = ImmutableHashSet.Create(
        "ArgumentNullException",
        "ArgumentException",
        "ArgumentOutOfRangeException",
        "InvalidOperationException",
        "NotImplementedException",
        "NotSupportedException",
        "ObjectDisposedException");

    internal static readonly ImmutableArray<(string Keyword, string ErrorMethod)> DomainExceptionKeywords =
        ImmutableArray.Create(
            ("NotFound", "NotFound"),
            ("Validation", "Validation"),
            ("Conflict", "Conflict"),
            ("Unauthorized", "Unauthorized"),
            ("Forbidden", "Forbidden"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ExceptionThrowingInHandler);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeThrow,
            SyntaxKind.ThrowStatement,
            SyntaxKind.ThrowExpression);
    }

    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context)
    {
        // Extract the object creation from the throw
        ExpressionSyntax? expression = context.Node switch
        {
            ThrowStatementSyntax throwStatement => throwStatement.Expression,
            ThrowExpressionSyntax throwExpression => throwExpression.Expression,
            _ => null
        };

        if (expression is not ObjectCreationExpressionSyntax
            and not ImplicitObjectCreationExpressionSyntax)
            return;

        // Get the exception type
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        var exceptionType = typeInfo.Type;
        if (exceptionType is null)
            return;

        var exceptionName = exceptionType.Name;

        // Skip excluded exceptions
        if (ExcludedExceptions.Contains(exceptionName))
            return;

        // Check if the exception name contains a domain exception keyword
        string? matchedMethod = null;
        foreach (var (keyword, errorMethod) in DomainExceptionKeywords)
        {
            if (exceptionName.Contains(keyword))
            {
                matchedMethod = errorMethod;
                break;
            }
        }

        if (matchedMethod is null)
            return;

        // Walk up to find enclosing Handle method in a handler class
        if (!IsInsideHandlerHandleMethod(context))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("ErrorMethod", matchedMethod);

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ExceptionThrowingInHandler,
            context.Node.GetLocation(),
            properties,
            matchedMethod,
            exceptionName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsInsideHandlerHandleMethod(SyntaxNodeAnalysisContext context)
    {
        // Walk up to find enclosing method named "Handle"
        var node = context.Node.Parent;
        MethodDeclarationSyntax? handleMethod = null;

        while (node is not null)
        {
            if (node is MethodDeclarationSyntax method)
            {
                if (method.Identifier.Text == "Handle")
                {
                    handleMethod = method;
                    break;
                }

                return false; // Inside a different method
            }

            if (node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax)
                break;

            // Stop at lambda/local function boundaries — throws inside
            // these are not directly in the Handle method's control flow
            if (node is LocalFunctionStatementSyntax or
                ParenthesizedLambdaExpressionSyntax or
                SimpleLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax)
                return false;

            node = node.Parent;
        }

        if (handleMethod is null)
            return false;

        // Find the containing class
        var classNode = handleMethod.Parent;
        while (classNode is not null && classNode is not ClassDeclarationSyntax)
            classNode = classNode.Parent;

        if (classNode is not ClassDeclarationSyntax classDeclaration)
            return false;

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (classSymbol is null)
            return false;

        return ImplementsHandlerInterface(classSymbol);
    }

    private static bool ImplementsHandlerInterface(INamedTypeSymbol typeSymbol)
    {
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            var originalDef = iface.OriginalDefinition;
            var ns = originalDef.ContainingNamespace?.ToDisplayString();
            var metadataName = originalDef.MetadataName;

            if (ns == MediatorNamespace && HandlerMetadataNames.Contains(metadataName))
                return true;
        }

        return false;
    }
}