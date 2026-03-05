using Microsoft.CodeAnalysis;

namespace Modulus.Generators;
internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor NonPartialStruct = new(
        id: "MODGEN001",
        title: "StronglyTypedId requires partial modifier",
        messageFormat: "Type '{0}' must be declared as partial to use [StronglyTypedId]",
        category: "ModulusGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonRecordStruct = new(
        id: "MODGEN002",
        title: "StronglyTypedId requires record struct",
        messageFormat: "Type '{0}' must be a record struct to use [StronglyTypedId]",
        category: "ModulusGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OpenGenericHandlerSkipped = new(
        id: "MODGEN003",
        title: "Open generic handler skipped for registration",
        messageFormat: "Type '{0}' is an open generic and cannot be registered by the handler registration generator",
        category: "ModulusGenerator",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncompleteModuleRegistration = new(
        id: "MODGEN004",
        title: "Incomplete IModuleRegistration implementation",
        messageFormat: "Type '{0}' implements IModuleRegistration but is missing static method '{1}'; it will be skipped from auto-registration",
        category: "ModulusGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}