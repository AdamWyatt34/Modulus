namespace Modulus.Cli.Validation;

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
