using System;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Modulus.Generators;

[Generator]
public sealed class StronglyTypedIdGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "Modulus.Mediator.Abstractions.StronglyTypedIdAttribute";
    private const string DbContextFullName = "Microsoft.EntityFrameworkCore.DbContext";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => GetModel(ctx))
            .Where(static m => m is not null);

        // Cacheable bool — same pattern ModuleRegistrationGenerator uses for IEndpointRouteBuilder.
        // Gating the EF Core ValueConverter on this means a project without an EF Core reference
        // (e.g. a Domain project, where strongly typed IDs are meant to live) never gets CS0234
        // from generated code that references a type it can't see.
        var hasEfCore = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName(DbContextFullName) is not null);

        var combined = provider.Combine(hasEfCore);

        context.RegisterSourceOutput(combined, static (spc, pair) => Execute(spc, pair.Left!.Value, pair.Right));
    }

    private static StronglyTypedIdResult? GetModel(GeneratorAttributeSyntaxContext context)
    {
        var structDeclaration = (TypeDeclarationSyntax)context.TargetNode;
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var location = EquatableLocation.FromLocation(structDeclaration.Identifier.GetLocation());

        // Validate: must be partial (check first — more actionable)
        var isPartial = structDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword);
        if (!isPartial)
        {
            return new StronglyTypedIdResult(
                null,
                DiagnosticDescriptors.NonPartialStruct,
                location,
                symbol.Name);
        }

        // Validate: must be a record struct
        if (!symbol.IsRecord)
        {
            return new StronglyTypedIdResult(
                null,
                DiagnosticDescriptors.NonRecordStruct,
                location,
                symbol.Name);
        }

        // Validate: must be top-level. A nested type would need a qualified partial declaration
        // to be extended; generating an unrelated top-level type of the same simple name is worse
        // than refusing, so this is a diagnostic instead.
        if (symbol.ContainingType is not null)
        {
            return new StronglyTypedIdResult(
                null,
                DiagnosticDescriptors.NestedStronglyTypedId,
                location,
                symbol.Name);
        }

        // Extract backing type from attribute constructor argument
        var backingType = BackingType.Guid;
        var attributeData = context.Attributes[0];
        if (attributeData.ConstructorArguments.Length > 0)
        {
            var arg = attributeData.ConstructorArguments[0];
            if (arg.Value is ITypeSymbol typeSymbol && !TryGetBackingType(typeSymbol, out backingType))
            {
                return new StronglyTypedIdResult(
                    null,
                    DiagnosticDescriptors.UnsupportedBackingType,
                    location,
                    symbol.Name);
            }
        }

        var namespaceName = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        var accessibility = symbol.DeclaredAccessibility == Accessibility.Internal ? "internal" : "public";

        return new StronglyTypedIdResult(
            new StronglyTypedIdModel(symbol.Name, namespaceName, backingType, accessibility),
            null,
            null,
            null);
    }

    private static bool TryGetBackingType(ITypeSymbol typeSymbol, out BackingType backingType)
    {
        switch (typeSymbol.SpecialType)
        {
            case SpecialType.System_Int32:
                backingType = BackingType.Int;
                return true;
            case SpecialType.System_Int64:
                backingType = BackingType.Long;
                return true;
        }

        if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid")
        {
            backingType = BackingType.Guid;
            return true;
        }

        // Unsupported (e.g. typeof(string)) — caller reports a diagnostic instead of silently
        // falling back to Guid.
        backingType = BackingType.Guid;
        return false;
    }

    private static void Execute(SourceProductionContext context, StronglyTypedIdResult result, bool includeEfCoreValueConverter)
    {
        if (result.Diagnostic is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                result.Diagnostic,
                result.DiagnosticLocation?.ToLocation() ?? Location.None,
                result.DiagnosticArg));
            return;
        }

        if (result.Model is null)
            return;

        var model = result.Model.Value;
        var source = GenerateSource(model, includeEfCoreValueConverter);
        context.AddSource(GetHintName(model), SourceText.From(source, Encoding.UTF8));
    }

    private static string GetHintName(StronglyTypedIdModel model)
    {
        // Namespace-qualified and sanitized so two [StronglyTypedId] structs with the same name
        // in different namespaces of one assembly don't collide on AddSource's hint name (which
        // would fault the generator with a duplicate-hintName exception and lose every generated
        // ID in the assembly).
        var qualifiedName = model.Namespace is null
            ? model.TypeName
            : $"{model.Namespace}.{model.TypeName}";

        return $"{Sanitize(qualifiedName)}.g.cs";
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '.' ? c : '_');
        }

        return sb.ToString();
    }

    internal static string GenerateSource(StronglyTypedIdModel model, bool includeEfCoreValueConverter)
    {
        var typeName = model.TypeName;
        var backingTypeName = GetBackingTypeName(model.BackingType);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (model.Namespace is not null)
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        // Partial struct with attributes — accessibility mirrors the declared type instead of
        // being hardcoded, so an `internal` strongly typed ID doesn't get a `public` partial
        // (CS0262-adjacent inconsistency) generated for it.
        sb.AppendLine($"[System.ComponentModel.TypeConverter(typeof({typeName}.{typeName}TypeConverter))]");
        sb.AppendLine($"[System.Text.Json.Serialization.JsonConverter(typeof({typeName}.{typeName}JsonConverter))]");
        sb.AppendLine($"{model.Accessibility} readonly partial record struct {typeName}");
        sb.AppendLine("{");
        sb.AppendLine($"    public {backingTypeName} Value {{ get; }}");
        sb.AppendLine();
        sb.AppendLine($"    public {typeName}({backingTypeName} value) => Value = value;");
        sb.AppendLine();

        if (model.BackingType == BackingType.Guid)
        {
            sb.AppendLine($"    public static {typeName} New() => new(System.Guid.NewGuid());");
            sb.AppendLine();
            sb.AppendLine($"    public static {typeName} Empty => new(System.Guid.Empty);");
        }
        else if (model.BackingType == BackingType.Long)
        {
            sb.AppendLine($"    public static {typeName} Empty => new(0L);");
        }
        else
        {
            sb.AppendLine($"    public static {typeName} Empty => new(0);");
        }

        sb.AppendLine();
        sb.AppendLine("    public override string ToString() => Value.ToString();");
        sb.AppendLine();

        if (includeEfCoreValueConverter)
        {
            // ValueConverter (nested inside the partial struct to stay in the same namespace).
            // Only emitted when the compilation references EF Core — otherwise this is CS0234 in
            // generated code, exactly in Domain projects where strongly typed IDs are meant to live.
            sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"Modulus.Generators.StronglyTypedIdGenerator\", \"{GeneratorVersion.Value}\")]");
            sb.AppendLine($"    public sealed class {typeName}ValueConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<{typeName}, {backingTypeName}>");
            sb.AppendLine("    {");
            sb.AppendLine($"        public {typeName}ValueConverter()");
            sb.AppendLine($"            : base(id => id.Value, value => new {typeName}(value)) {{ }}");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // JsonConverter
        var (jsonRead, jsonWrite) = model.BackingType switch
        {
            BackingType.Int => ("reader.GetInt32()", "writer.WriteNumberValue(value.Value)"),
            BackingType.Long => ("reader.GetInt64()", "writer.WriteNumberValue(value.Value)"),
            _ => ("reader.GetGuid()", "writer.WriteStringValue(value.Value)")
        };

        sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"Modulus.Generators.StronglyTypedIdGenerator\", \"{GeneratorVersion.Value}\")]");
        sb.AppendLine($"    public sealed class {typeName}JsonConverter : System.Text.Json.Serialization.JsonConverter<{typeName}>");
        sb.AppendLine("    {");
        sb.AppendLine($"        public override {typeName} Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine($"            => new({jsonRead});");
        sb.AppendLine();
        sb.AppendLine($"        public override void Write(System.Text.Json.Utf8JsonWriter writer, {typeName} value, System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine($"            => {jsonWrite};");
        sb.AppendLine("    }");
        sb.AppendLine();

        // TypeConverter — parses with CultureInfo.InvariantCulture so a numeric ID's textual
        // round-trip is independent of the current thread's culture (e.g. thousands separators).
        var parseExpr = model.BackingType switch
        {
            BackingType.Int => "int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)",
            BackingType.Long => "long.Parse(s, System.Globalization.CultureInfo.InvariantCulture)",
            _ => "System.Guid.Parse(s)"
        };

        sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"Modulus.Generators.StronglyTypedIdGenerator\", \"{GeneratorVersion.Value}\")]");
        sb.AppendLine($"    public sealed class {typeName}TypeConverter : System.ComponentModel.TypeConverter");
        sb.AppendLine("    {");
        sb.AppendLine("        public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Type sourceType)");
        sb.AppendLine("            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);");
        sb.AppendLine();
        sb.AppendLine("        public override object? ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)");
        sb.AppendLine($"            => value is string s ? new {typeName}({parseExpr}) : base.ConvertFrom(context, culture, value);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetBackingTypeName(BackingType backingType)
    {
        return backingType switch
        {
            BackingType.Int => "int",
            BackingType.Long => "long",
            _ => "System.Guid"
        };
    }
}

internal enum BackingType
{
    Guid,
    Int,
    Long
}

internal readonly struct StronglyTypedIdModel : IEquatable<StronglyTypedIdModel>
{
    public string TypeName { get; }
    public string? Namespace { get; }
    public BackingType BackingType { get; }
    public string Accessibility { get; }

    public StronglyTypedIdModel(string typeName, string? ns, BackingType backingType, string accessibility)
    {
        TypeName = typeName;
        Namespace = ns;
        BackingType = backingType;
        Accessibility = accessibility;
    }

    public bool Equals(StronglyTypedIdModel other) =>
        TypeName == other.TypeName &&
        Namespace == other.Namespace &&
        BackingType == other.BackingType &&
        Accessibility == other.Accessibility;

    public override bool Equals(object obj) =>
        obj is StronglyTypedIdModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = TypeName?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (Namespace?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (int)BackingType;
            hash = (hash * 397) ^ (Accessibility?.GetHashCode() ?? 0);
            return hash;
        }
    }
}

internal readonly struct StronglyTypedIdResult : IEquatable<StronglyTypedIdResult>
{
    public StronglyTypedIdModel? Model { get; }
    public DiagnosticDescriptor? Diagnostic { get; }
    public EquatableLocation? DiagnosticLocation { get; }
    public string? DiagnosticArg { get; }

    public StronglyTypedIdResult(
        StronglyTypedIdModel? model,
        DiagnosticDescriptor? diagnostic,
        EquatableLocation? diagnosticLocation,
        string? diagnosticArg)
    {
        Model = model;
        Diagnostic = diagnostic;
        DiagnosticLocation = diagnosticLocation;
        DiagnosticArg = diagnosticArg;
    }

    public bool Equals(StronglyTypedIdResult other) =>
        Equals(Model, other.Model) &&
        Equals(Diagnostic, other.Diagnostic) &&
        Equals(DiagnosticLocation, other.DiagnosticLocation) &&
        DiagnosticArg == other.DiagnosticArg;

    public override bool Equals(object obj) =>
        obj is StronglyTypedIdResult other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Model?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (Diagnostic?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (DiagnosticLocation?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (DiagnosticArg?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
