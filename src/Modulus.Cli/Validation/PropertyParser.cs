using System.Collections.Generic;
using System.Linq;
using Modulus.Templates;

namespace Modulus.Cli.Validation;

public static class PropertyParser
{
    /// <summary>
    /// The entity base classes (<c>Entity&lt;TId&gt;</c> / <c>AggregateRoot&lt;TId&gt;</c>)
    /// already declare an <c>Id</c> property, and <c>EntityGenerator</c>'s factory method always
    /// takes a hard-coded <c>id</c> parameter for it. A user-supplied property that collides with
    /// "Id" (in any casing — the factory's camelCase parameter name collapses "Id"/"id" to the
    /// same "id" either way) produces an uncompilable entity: a duplicate member initializer
    /// (CS1912) and/or a duplicate factory parameter (CS0100).
    /// </summary>
    private const string ReservedIdName = "Id";

    public static (IReadOnlyList<EntityProperty> Properties, string? Error) Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ([], null);

        var results = new List<EntityProperty>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = SplitTopLevel(input);

        foreach (var part in parts)
        {
            var colonIndex = part.IndexOf(':');
            if (colonIndex < 1 || colonIndex == part.Length - 1)
                return ([], $"Invalid property format: '{part}'. Expected 'Name:Type'.");

            var name = part[..colonIndex].Trim();
            var type = part[(colonIndex + 1)..].Trim();

            if (!CSharpIdentifierValidator.IsValid(name))
                return ([], $"Property name '{name}' is not a valid C# identifier.");

            if (string.Equals(name, ReservedIdName, StringComparison.OrdinalIgnoreCase))
                return ([], $"Property name '{name}' is reserved: every generated entity already declares 'Id' from its base class.");

            if (!seenNames.Add(name))
                return ([], $"Property name '{name}' is specified more than once.");

            if (!CSharpIdentifierValidator.IsValidTypeName(type))
                return ([], $"Property type '{type}' is not a valid C# type name.");

            results.Add(new EntityProperty(name, type));
        }

        return (results, null);
    }

    /// <summary>
    /// Splits a <c>--properties</c> value on top-level commas only — commas nested inside a
    /// generic type argument list (e.g. <c>Prices:Dictionary&lt;string,decimal&gt;</c>) are part
    /// of that property's type, not a separator between properties. Tracks '&lt;'/'&gt;' nesting
    /// depth across the whole input; a comma is only treated as a separator at depth zero.
    /// Mirrors the previous <c>string.Split(',', RemoveEmptyEntries | TrimEntries)</c> behavior
    /// for whitespace and empty-entry handling (e.g. a trailing comma is silently dropped).
    /// </summary>
    private static List<string> SplitTopLevel(string input)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < input.Length; i++)
        {
            switch (input[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    if (depth > 0)
                        depth--;
                    break;
                case ',' when depth == 0:
                    AddTrimmed(parts, input[start..i]);
                    start = i + 1;
                    break;
            }
        }

        AddTrimmed(parts, input[start..]);
        return parts;
    }

    private static void AddTrimmed(List<string> parts, string part)
    {
        var trimmed = part.Trim();
        if (trimmed.Length > 0)
        {
            parts.Add(trimmed);
        }
    }
}
