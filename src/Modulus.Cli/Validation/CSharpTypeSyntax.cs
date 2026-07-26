namespace Modulus.Cli.Validation;

/// <summary>
/// A small recursive-descent validator for C# type-reference syntax, used by
/// <see cref="CSharpIdentifierValidator.IsValidTypeName"/> to accept <c>--result-type</c> and
/// <c>--properties</c> type values.
/// </summary>
/// <remarks>
/// <para>
/// Grammar (informally):
/// <code>
/// type          := nonArrayType '?'? arrayRank*
/// nonArrayType  := qualifiedName genericArgs?
/// genericArgs   := '&lt;' type (',' type)* '&gt;'
/// qualifiedName := identifier ('.' identifier)*
/// arrayRank     := '[' ','* ']' '?'?
/// </code>
/// Every identifier segment is checked against the C# reserved-keyword list, with the same
/// built-in-alias bypass <c>IsValid</c> needs (<c>int</c>, <c>string</c>, ... are keywords but
/// are legal type references). This mirrors <c>CSharpIdentifierValidator.IsValid</c>'s previous
/// per-type-name behavior but applies it uniformly to every segment — including generic
/// container names and type arguments — rather than only to the outermost, non-generic case.
/// </para>
/// <para>
/// SECURITY CONTRACT: the only characters this grammar can ever consume are identifier
/// characters (letters, digits, underscore), '.', '&lt;', '&gt;', ',', '?', '[', ']', and
/// whitespace immediately around a generic argument separator. Any other character — or any
/// malformed nesting (unbalanced brackets, an empty generic argument list, trailing garbage) —
/// causes parsing to stop short of the end of the input, which <see cref="IsValid"/> rejects.
/// Widening this grammar therefore never widens the character surface spliced into the
/// C#-only contexts <c>ResultType</c>/property-type values are used in (see the call sites
/// documented on <see cref="CSharpIdentifierValidator.IsValidTypeName"/>).
/// </para>
/// </remarks>
internal static class CSharpTypeSyntax
{
    public static bool IsValid(string type)
    {
        if (string.IsNullOrEmpty(type))
            return false;

        var pos = 0;
        return TryParseType(type, ref pos) && pos == type.Length;
    }

    private static bool TryParseType(string s, ref int pos)
    {
        if (!TryParseNonArrayType(s, ref pos))
            return false;

        TryConsume(s, ref pos, '?');

        while (pos < s.Length && s[pos] == '[')
        {
            var rankStart = pos;
            pos++; // consume '['

            while (pos < s.Length && s[pos] == ',')
                pos++;

            if (pos >= s.Length || s[pos] != ']')
            {
                // Not a well-formed array rank — roll back and stop; any leftover text fails
                // the caller's "fully consumed" check.
                pos = rankStart;
                break;
            }

            pos++; // consume ']'
            TryConsume(s, ref pos, '?');
        }

        return true;
    }

    private static bool TryParseNonArrayType(string s, ref int pos)
    {
        if (!TryParseQualifiedName(s, ref pos))
            return false;

        if (pos < s.Length && s[pos] == '<')
        {
            var beforeGenericArgs = pos;
            pos++; // tentatively consume '<'

            if (TryParseTypeArgList(s, ref pos) && pos < s.Length && s[pos] == '>')
            {
                pos++; // consume '>'
            }
            else
            {
                // Not a valid generic argument list — roll back to just after the bare name.
                // Any leftover '<...' fails the caller's "fully consumed" check rather than
                // silently degrading a malformed generic into a valid non-generic type.
                pos = beforeGenericArgs;
            }
        }

        return true;
    }

    private static bool TryParseTypeArgList(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (!TryParseType(s, ref pos))
            return false;
        SkipWhitespace(s, ref pos);

        while (pos < s.Length && s[pos] == ',')
        {
            pos++;
            SkipWhitespace(s, ref pos);
            if (!TryParseType(s, ref pos))
                return false;
            SkipWhitespace(s, ref pos);
        }

        return true;
    }

    private static bool TryParseQualifiedName(string s, ref int pos)
    {
        if (!TryParseIdentifierSegment(s, ref pos))
            return false;

        while (pos < s.Length && s[pos] == '.')
        {
            var dotPos = pos;
            pos++;

            if (!TryParseIdentifierSegment(s, ref pos))
            {
                pos = dotPos;
                return false;
            }
        }

        return true;
    }

    private static bool TryParseIdentifierSegment(string s, ref int pos)
    {
        var start = pos;

        if (pos >= s.Length || (!char.IsLetter(s[pos]) && s[pos] != '_'))
            return false;

        pos++;
        while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_'))
            pos++;

        var segment = s[start..pos];

        // Built-in aliases (int, string, object, ...) are keywords but are legal type names;
        // every other keyword (class, namespace, ...) cannot be a type reference.
        if (CSharpIdentifierValidator.IsReservedKeyword(segment)
            && !CSharpIdentifierValidator.BuiltInTypeAliases.Contains(segment))
        {
            pos = start;
            return false;
        }

        return true;
    }

    private static void TryConsume(string s, ref int pos, char c)
    {
        if (pos < s.Length && s[pos] == c)
            pos++;
    }

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && s[pos] == ' ')
            pos++;
    }
}
