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
    /// Validates a C# <b>type reference</b> — used for <c>--result-type</c> on add-query/
    /// add-command/add-endpoint, and for property types parsed by <c>PropertyParser</c> — as
    /// opposed to <see cref="IsValid"/>, which validates a C# <b>identifier</b> (a name that will
    /// be declared, not referenced as a type).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Delegates to <see cref="CSharpTypeSyntax"/>, a small recursive-descent grammar that accepts
    /// built-in aliases (<c>string</c>, <c>int</c>, ...), fully-qualified dotted identifier
    /// chains, arbitrarily nested generic type arguments (<c>List&lt;T&gt;</c>,
    /// <c>Dictionary&lt;string, List&lt;Guid&gt;&gt;</c>, ...), nullable value/reference types
    /// (a trailing <c>?</c>), and array ranks (<c>T[]</c>, jagged <c>T[][]</c>, multi-dimensional
    /// <c>T[,]</c>) — anywhere in the type, e.g. <c>int?[]</c> or <c>List&lt;int&gt;[]</c>.
    /// </para>
    /// <para>
    /// This is the single source of truth both add-query/add-command/add-endpoint's
    /// <c>--result-type</c> validation and <c>PropertyParser</c>'s per-property type validation
    /// delegate to, so the accepted type surface never drifts between the two call sites.
    /// </para>
    /// </remarks>
    public static bool IsValidTypeName(string type) => CSharpTypeSyntax.IsValid(type);

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

    internal static bool IsReservedKeyword(string segment) => CSharpKeywords.Contains(segment);

    internal static readonly HashSet<string> BuiltInTypeAliases = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "char", "decimal", "double", "float",
        "int", "uint", "long", "ulong", "short", "ushort", "string",
        "object", "nint", "nuint",
    };
}
