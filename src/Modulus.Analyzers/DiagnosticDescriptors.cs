using Microsoft.CodeAnalysis;

namespace Modulus.Analyzers;
internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor ModuleBoundaryViolation = new(
        id: "MOD001",
        title: "Module boundary violation",
        messageFormat: "Module '{0}' cannot reference '{1}'. Cross-module references are only allowed through Integration projects",
        category: "ModulusAnalyzer",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HandlerReturnTypeViolation = new(
        id: "MOD002",
        title: "Handler should return Result or Result<T>",
        messageFormat: "Handler '{0}' should return 'Result' or 'Result<T>'. Use the Result pattern for consistent error handling",
        category: "ModulusAnalyzer",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ExceptionThrowingInHandler = new(
        id: "MOD003",
        title: "Use Result pattern instead of throwing domain exceptions in handlers",
        messageFormat: "Use 'Error.{0}(...)' instead of throwing '{1}' in handlers. Return errors as Result values",
        category: "ModulusAnalyzer",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DomainInfrastructureLeak = new(
        id: "MOD004",
        title: "Infrastructure concern detected in Domain layer",
        messageFormat: "Domain layer should not contain infrastructure concerns. '{0}' should be configured in the Infrastructure layer instead",
        category: "ModulusAnalyzer",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PublicSetterOnEntity = new(
        id: "MOD005",
        title: "Entity property should not have a public setter",
        messageFormat: "Entity properties should use private setters. Modify state through domain methods",
        category: "ModulusAnalyzer",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}