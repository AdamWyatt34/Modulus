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
