using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
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
    private const string IParsableFullName = "System.IParsable`1";

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

        // IParsable<T> has static abstract members, so declaring `: System.IParsable<T>` on the
        // generated partial requires the consumer's TFM to define the interface (.NET 7+). Modulus
        // is net10.0-only today, but this generator ships as a netstandard2.0 analyzer that could
        // in principle be referenced from an older-TFM project — gate the interface declaration
        // the same way as the EF Core check so that scenario degrades gracefully instead of
        // CS0246. The plain Parse/TryParse(string) methods are emitted unconditionally: minimal
        // API route/query binding discovers a static TryParse by signature convention, it does not
        // require the type to actually implement IParsable<T>.
        var hasIParsable = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName(IParsableFullName) is not null);

        var capabilities = hasEfCore.Combine(hasIParsable);
        var combined = provider.Combine(capabilities);

        context.RegisterSourceOutput(combined, static (spc, pair) =>
            Execute(spc, pair.Left!.Value, pair.Right.Left, pair.Right.Right));

        // --- Bulk EF Core conversion registration -----------------------------------------------
        // One generated helper per assembly that registers every [StronglyTypedId]'s ValueConverter
        // in a single ConfigureConventions call, so a DbContext doesn't need a HasConversion<>()
        // line per property per entity. Gated on the same hasEfCore bool as the per-ID converter —
        // a project without an EF Core reference never sees this file either.
        var localEntries = provider
            .Where(static m => m!.Value.Model is not null)
            .Select(static (m, _) => StronglyTypedIdConventionEntry.FromModel(m!.Value.Model!.Value))
            .Collect();

        var referencedEntries = context.CompilationProvider
            .Select(static (compilation, ct) => FindReferencedStronglyTypedIds(compilation, ct));

        var allEntries = localEntries.Combine(referencedEntries)
            .Select(static (pair, _) =>
                new EquatableArray<StronglyTypedIdConventionEntry>(pair.Left.AddRange(pair.Right.Array)));

        var rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns);
                return ns;
            });

        var assemblyName = context.CompilationProvider
            .Select(static (c, _) => c.AssemblyName);

        var namespaceInfo = rootNamespace.Combine(assemblyName);
        var bulkCombined = allEntries.Combine(namespaceInfo).Combine(hasEfCore);

        context.RegisterSourceOutput(bulkCombined, static (spc, data) =>
            ExecuteBulkRegistration(spc, data.Left.Left, data.Left.Right.Left, data.Left.Right.Right, data.Right));
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
            case SpecialType.System_String:
                backingType = BackingType.String;
                return true;
        }

        if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid")
        {
            backingType = BackingType.Guid;
            return true;
        }

        // Unsupported (e.g. typeof(decimal)) — caller reports a diagnostic instead of silently
        // falling back to Guid.
        backingType = BackingType.Guid;
        return false;
    }

    private static void Execute(
        SourceProductionContext context,
        StronglyTypedIdResult result,
        bool includeEfCoreValueConverter,
        bool includeIParsableInterface)
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
        var source = GenerateSource(model, includeEfCoreValueConverter, includeIParsableInterface);
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

    internal static string GenerateSource(
        StronglyTypedIdModel model,
        bool includeEfCoreValueConverter,
        bool includeIParsableInterface)
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
        // (CS0262-adjacent inconsistency) generated for it. IComparable<T> is unconditional (every
        // backing type supports it); IParsable<T> is gated because it carries static abstract
        // members that require a .NET 7+ target — see the hasIParsable comment in Initialize.
        var interfaceList = includeIParsableInterface
            ? $"System.IComparable<{typeName}>, System.IParsable<{typeName}>"
            : $"System.IComparable<{typeName}>";

        sb.AppendLine($"[System.ComponentModel.TypeConverter(typeof({typeName}.{typeName}TypeConverter))]");
        sb.AppendLine($"[System.Text.Json.Serialization.JsonConverter(typeof({typeName}.{typeName}JsonConverter))]");
        sb.AppendLine($"{model.Accessibility} readonly partial record struct {typeName} : {interfaceList}");
        sb.AppendLine("{");
        sb.AppendLine($"    public {backingTypeName} Value {{ get; }}");
        sb.AppendLine();

        if (model.BackingType == BackingType.String)
        {
            // No natural sentinel for "no value" the way Guid.Empty/0 are, and `default(TId)` (e.g.
            // a value carved out of an array, or `default` in generic code) bypasses this
            // constructor entirely, leaving Value null despite the non-nullable `string` type — so
            // ToString()/CompareTo below defensively null-coalesce instead of trusting Value here.
            sb.AppendLine($"    public {typeName}(string value) => Value = value ?? throw new System.ArgumentNullException(nameof(value));");
        }
        else
        {
            sb.AppendLine($"    public {typeName}({backingTypeName} value) => Value = value;");
        }

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
        else if (model.BackingType == BackingType.String)
        {
            sb.AppendLine($"    public static {typeName} Empty => new(string.Empty);");
        }
        else
        {
            sb.AppendLine($"    public static {typeName} Empty => new(0);");
        }

        sb.AppendLine();

        if (model.BackingType == BackingType.String)
        {
            sb.AppendLine("    public override string ToString() => Value ?? string.Empty;");
        }
        else
        {
            sb.AppendLine("    public override string ToString() => Value.ToString();");
        }

        sb.AppendLine();

        // IComparable<T> — delegates to the backing value's own comparison. `string`'s default
        // instance `CompareTo` would throw on a null Value (see the constructor comment above), so
        // it goes through the null-tolerant static `string.CompareOrdinal` instead; ordinal (not
        // culture-aware) so sort order for a numeric-looking string ID doesn't shift with the
        // current thread's culture.
        var compareExpr = model.BackingType == BackingType.String
            ? "string.CompareOrdinal(Value, other.Value)"
            : "Value.CompareTo(other.Value)";

        sb.AppendLine($"    public int CompareTo({typeName} other) => {compareExpr};");
        sb.AppendLine();

        // Parse/TryParse — always emitted (regardless of includeIParsableInterface) so
        // `{Type}.TryParse(...)` and minimal API route/query binding (which discovers this method
        // by signature convention) work even on a TFM too old to declare `: IParsable<T>`.
        // Numeric/Guid parsing always uses InvariantCulture rather than the passed-in
        // IFormatProvider, matching the TypeConverter below — an ID's textual round-trip should not
        // depend on the current thread's culture, which the IParsable contract would otherwise
        // allow.
        AppendParseMembers(sb, typeName, model.BackingType);

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
            BackingType.String => ("reader.GetString()!", "writer.WriteStringValue(value.Value)"),
            _ => ("reader.GetGuid()", "writer.WriteStringValue(value.Value)")
        };

        // Dictionary-key support (System.Text.Json): without ReadAsPropertyName/WriteAsPropertyName,
        // serializing e.g. Dictionary<OrderId, T> throws NotSupportedException at runtime, because
        // the base JsonConverter<T> only handles values, never property names. Inside
        // ReadAsPropertyName the reader is positioned on a PropertyName token, not a String/Number
        // value token, so it must go through reader.GetString() and a manual parse rather than
        // reader.GetGuid()/GetInt32() (which require a value token).
        var (jsonReadAsPropertyName, jsonWriteAsPropertyName) = model.BackingType switch
        {
            BackingType.Int => (
                "int.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture)",
                "writer.WritePropertyName(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))"),
            BackingType.Long => (
                "long.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture)",
                "writer.WritePropertyName(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))"),
            BackingType.String => (
                "reader.GetString()!",
                "writer.WritePropertyName(value.Value)"),
            _ => (
                "System.Guid.Parse(reader.GetString()!)",
                "writer.WritePropertyName(value.Value.ToString())")
        };

        sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"Modulus.Generators.StronglyTypedIdGenerator\", \"{GeneratorVersion.Value}\")]");
        sb.AppendLine($"    public sealed class {typeName}JsonConverter : System.Text.Json.Serialization.JsonConverter<{typeName}>");
        sb.AppendLine("    {");
        sb.AppendLine($"        public override {typeName} Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine($"            => new({jsonRead});");
        sb.AppendLine();
        sb.AppendLine($"        public override void Write(System.Text.Json.Utf8JsonWriter writer, {typeName} value, System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine($"            => {jsonWrite};");
        sb.AppendLine();
        sb.AppendLine($"        public override {typeName} ReadAsPropertyName(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine($"            => new({jsonReadAsPropertyName});");
        sb.AppendLine();
        sb.AppendLine($"        public override void WriteAsPropertyName(System.Text.Json.Utf8JsonWriter writer, {typeName} value, System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine($"            => {jsonWriteAsPropertyName};");
        sb.AppendLine("    }");
        sb.AppendLine();

        // TypeConverter — parses with CultureInfo.InvariantCulture so a numeric ID's textual
        // round-trip is independent of the current thread's culture (e.g. thousands separators).
        // `string` needs no parsing at all — the route/config value already is the backing value.
        var parseExpr = model.BackingType switch
        {
            BackingType.Int => "int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)",
            BackingType.Long => "long.Parse(s, System.Globalization.CultureInfo.InvariantCulture)",
            BackingType.String => "s",
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

    private static void AppendParseMembers(StringBuilder sb, string typeName, BackingType backingType)
    {
        string parseExpr;
        string tryParseBody;

        switch (backingType)
        {
            case BackingType.Int:
                parseExpr = "int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)";
                tryParseBody =
                    $"if (int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))";
                break;
            case BackingType.Long:
                parseExpr = "long.Parse(s, System.Globalization.CultureInfo.InvariantCulture)";
                tryParseBody =
                    $"if (long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))";
                break;
            case BackingType.String:
                parseExpr = "s";
                tryParseBody = "if (s is not null)";
                break;
            default:
                parseExpr = "System.Guid.Parse(s)";
                tryParseBody = "if (System.Guid.TryParse(s, out var value))";
                break;
        }

        sb.AppendLine($"    public static {typeName} Parse(string s, System.IFormatProvider? provider) => new({parseExpr});");
        sb.AppendLine();
        sb.AppendLine($"    public static bool TryParse(string? s, System.IFormatProvider? provider, out {typeName} result)");
        sb.AppendLine("    {");
        sb.AppendLine($"        {tryParseBody}");
        sb.AppendLine("        {");

        // The `string` backing type has no separate parsed `value` local — `s` itself (already
        // proven non-null by the guard above) is the backing value.
        sb.AppendLine(backingType == BackingType.String
            ? "            result = new(s!);"
            : "            result = new(value);");

        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        result = default;");
        sb.AppendLine("        return false;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static string GetBackingTypeName(BackingType backingType)
    {
        return backingType switch
        {
            BackingType.Int => "int",
            BackingType.Long => "long",
            BackingType.String => "string",
            _ => "System.Guid"
        };
    }

    private static EquatableArray<StronglyTypedIdConventionEntry> FindReferencedStronglyTypedIds(
        Compilation compilation, CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<StronglyTypedIdConventionEntry>();

        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ct.ThrowIfCancellationRequested();

            // Skip the BCL/framework closure and any assembly that couldn't possibly define a
            // [StronglyTypedId] — same pre-filter as HandlerRegistrationGenerator/
            // ModuleRegistrationGenerator use for their own referenced-assembly walks.
            if (!ReferencedAssemblyFilter.ShouldWalk(assemblySymbol))
                continue;

            CollectStronglyTypedIdsFromNamespace(assemblySymbol.GlobalNamespace, builder, ct);
        }

        return new EquatableArray<StronglyTypedIdConventionEntry>(builder.ToImmutable());
    }

    private static void CollectStronglyTypedIdsFromNamespace(
        INamespaceSymbol ns,
        ImmutableArray<StronglyTypedIdConventionEntry>.Builder builder,
        CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();

            // Cross-assembly: only a public ID is nameable from the generated call site here
            // (an internal one is CS0122 without InternalsVisibleTo). And only when that
            // assembly's own build actually emitted the nested ValueConverter — checked against
            // real metadata instead of assumed, because a Domain project can legitimately build
            // without an EF Core reference, in which case its own StronglyTypedIdGenerator run
            // never generated one.
            if (type.DeclaredAccessibility == Accessibility.Public && HasStronglyTypedIdAttribute(type))
            {
                var converterName = type.Name + "ValueConverter";
                if (!type.GetTypeMembers(converterName).IsEmpty)
                {
                    var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    builder.Add(new StronglyTypedIdConventionEntry(fqn, $"{fqn}.{converterName}"));
                }
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            CollectStronglyTypedIdsFromNamespace(childNs, builder, ct);
        }
    }

    private static bool HasStronglyTypedIdAttribute(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "StronglyTypedIdAttribute" &&
                attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Modulus.Mediator.Abstractions")
                return true;
        }

        return false;
    }

    private static void ExecuteBulkRegistration(
        SourceProductionContext context,
        EquatableArray<StronglyTypedIdConventionEntry> entries,
        string? rootNamespace,
        string? assemblyName,
        bool hasEfCore)
    {
        // Same gate as the per-ID ValueConverter: a project without an EF Core reference never
        // sees ModelConfigurationBuilder, so this whole file would be CS0234 there. Also skip when
        // there is nothing to register — an empty helper method adds no value.
        if (!hasEfCore || entries.IsEmpty)
            return;

        var ns = rootNamespace ?? assemblyName ?? "GeneratedRegistrations";

        var ordered = entries.Array
            .Distinct()
            .OrderBy(e => e.TypeFullyQualifiedName, StringComparer.Ordinal)
            .ToImmutableArray();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"[global::System.CodeDom.Compiler.GeneratedCode(\"Modulus.Generators.StronglyTypedIdGenerator\", \"{GeneratorVersion.Value}\")]");
        sb.AppendLine("public static class ModulusStronglyTypedIdConventions");
        sb.AppendLine("{");
        sb.AppendLine("    // Registers the generated EF Core ValueConverter for every [StronglyTypedId] found in");
        sb.AppendLine("    // this compilation (plus public ones from referenced assemblies that were themselves");
        sb.AppendLine("    // built with an EF Core reference) in a single call. Invoke from DbContext.ConfigureConventions.");
        sb.AppendLine("    public static Microsoft.EntityFrameworkCore.ModelConfigurationBuilder UseModulusStronglyTypedIds(");
        sb.AppendLine("        this Microsoft.EntityFrameworkCore.ModelConfigurationBuilder configurationBuilder)");
        sb.AppendLine("    {");

        foreach (var entry in ordered)
        {
            sb.AppendLine($"        configurationBuilder.Properties<{entry.TypeFullyQualifiedName}>().HaveConversion<{entry.ConverterFullyQualifiedName}>();");
        }

        sb.AppendLine("        return configurationBuilder;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource(
            "ModulusStronglyTypedIdConventions.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
    }
}

internal enum BackingType
{
    Guid,
    Int,
    Long,
    String
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

/// <summary>
/// One row of the bulk EF Core registration helper: the strongly typed ID's fully qualified name
/// and its nested ValueConverter's fully qualified name. Kept separate from
/// <see cref="StronglyTypedIdModel"/> because entries collected from referenced assemblies (a
/// compiled <see cref="INamedTypeSymbol"/>, not a local <see cref="StronglyTypedIdModel"/>) only
/// ever need these two strings, not the backing type or declared accessibility.
/// </summary>
internal readonly struct StronglyTypedIdConventionEntry : IEquatable<StronglyTypedIdConventionEntry>
{
    public string TypeFullyQualifiedName { get; }
    public string ConverterFullyQualifiedName { get; }

    public StronglyTypedIdConventionEntry(string typeFullyQualifiedName, string converterFullyQualifiedName)
    {
        TypeFullyQualifiedName = typeFullyQualifiedName;
        ConverterFullyQualifiedName = converterFullyQualifiedName;
    }

    public static StronglyTypedIdConventionEntry FromModel(StronglyTypedIdModel model)
    {
        // `global::`-prefixed, built directly from the (validated top-level) namespace/type name
        // rather than round-tripped through a symbol — this runs for every locally-declared ID on
        // every keystroke, so avoiding a second symbol lookup keeps the pipeline cheap and cacheable.
        var fqn = model.Namespace is null
            ? $"global::{model.TypeName}"
            : $"global::{model.Namespace}.{model.TypeName}";

        return new StronglyTypedIdConventionEntry(fqn, $"{fqn}.{model.TypeName}ValueConverter");
    }

    public bool Equals(StronglyTypedIdConventionEntry other) =>
        TypeFullyQualifiedName == other.TypeFullyQualifiedName &&
        ConverterFullyQualifiedName == other.ConverterFullyQualifiedName;

    public override bool Equals(object obj) =>
        obj is StronglyTypedIdConventionEntry other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = TypeFullyQualifiedName?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (ConverterFullyQualifiedName?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
