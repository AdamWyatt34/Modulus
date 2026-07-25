namespace Modulus.Cli.Validation;

/// <summary>
/// Validates that a string is a legal C# identifier (letters, digits, underscores; no leading digit; not a keyword).
/// </summary>
/// <remarks>
/// <para>
/// SECURITY CONTRACT — this validator is also the implicit defense that makes raw
/// <see cref="string.Replace(string, string?)"/> token substitution safe across XML (.csproj),
/// JSON (appsettings.json), and C# template contexts. By rejecting every character that could
/// break out of any of those formats (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>"</c>, <c>'</c>,
/// <c>;</c>, <c>{</c>, <c>}</c>, <c>:</c>, <c>(</c>, <c>)</c>, <c>.</c>, whitespace, shell metacharacters)
/// the validator prevents template injection without the engine doing context-aware encoding.
/// </para>
/// <para>
/// If you relax the rule to allow new characters (e.g., dots for fully-qualified namespaces),
/// audit every template-rendered output format and add context-appropriate encoding to
/// <c>TemplateEngine</c>.
/// </para>
/// </remarks>
public static class CSharpIdentifierValidator
{
    public static bool IsValid(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        }

        return !CSharpKeywords.Contains(name);
    }

    /// <summary>
    /// Validates a C# <b>type name</b> — used for <c>--result-type</c> on add-query/add-command/
    /// add-endpoint, and for property types parsed by <c>PropertyParser</c> — as opposed to
    /// <see cref="IsValid"/>, which validates a C# <b>identifier</b> (a name that will be
    /// declared, not referenced as a type). Built-in type aliases (<c>string</c>, <c>int</c>,
    /// <c>bool</c>, ...) and common BCL types (<c>Guid</c>, <c>DateTime</c>, ...) are accepted
    /// even though several of them are C# keywords that <see cref="IsValid"/> correctly rejects —
    /// the scaffold's own sample query returns <c>IQuery&lt;string&gt;</c>, so rejecting it here
    /// would make the scaffold reject its own shape. A trailing <c>?</c> (nullable) is stripped
    /// before validation. Does not support generic type arguments (e.g. <c>List&lt;T&gt;</c>) or
    /// array types — those remain unsupported until the CLI gains a real type-syntax parser.
    /// </summary>
    /// <remarks>
    /// This is the single source of truth both add-query/add-command/add-endpoint's
    /// <c>--result-type</c> validation and <c>PropertyParser</c>'s per-property type validation
    /// delegate to, so the accepted type surface never drifts between the two call sites.
    /// </remarks>
    public static bool IsValidTypeName(string type)
    {
        if (string.IsNullOrEmpty(type))
            return false;

        // Strip a trailing nullable marker, e.g. "int?", "Guid?".
        var baseType = type.TrimEnd('?');
        if (baseType.Length == 0)
            return false;

        if (BuiltInTypeAliases.Contains(baseType) || CommonFrameworkTypes.Contains(baseType))
            return true;

        // Custom or fully-qualified names: every dot-separated segment must be a valid
        // identifier. This still rejects the characters IsValid already guards against
        // (angle brackets, parens, semicolons, ...) — it just additionally allows the dot
        // separator between segments.
        var segments = baseType.Split('.');
        return segments.Length > 0 && segments.All(IsValid);
    }

    private static readonly HashSet<string> BuiltInTypeAliases = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "char", "decimal", "double", "float",
        "int", "uint", "long", "ulong", "short", "ushort", "string",
        "object", "nint", "nuint",
    };

    private static readonly HashSet<string> CommonFrameworkTypes = new(StringComparer.Ordinal)
    {
        "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan",
    };

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal",
        "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void",
        "volatile", "while",
    };
}
