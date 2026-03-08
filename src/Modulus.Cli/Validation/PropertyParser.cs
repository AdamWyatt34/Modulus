using System.Collections.Generic;
using System.Linq;
using Modulus.Templates;

namespace Modulus.Cli.Validation;

public static class PropertyParser
{
    public static (IReadOnlyList<EntityProperty> Properties, string? Error) Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ([], null);

        var results = new List<EntityProperty>();
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var colonIndex = part.IndexOf(':');
            if (colonIndex < 1 || colonIndex == part.Length - 1)
                return ([], $"Invalid property format: '{part}'. Expected 'Name:Type'.");

            var name = part[..colonIndex].Trim();
            var type = part[(colonIndex + 1)..].Trim();

            if (!CSharpIdentifierValidator.IsValid(name))
                return ([], $"Property name '{name}' is not a valid C# identifier.");

            if (!IsValidTypeName(type))
                return ([], $"Property type '{type}' is not a valid C# type name.");

            results.Add(new EntityProperty(name, type));
        }

        return (results, null);
    }

    private static bool IsValidTypeName(string type)
    {
        // Strip nullable suffix
        var baseType = type.TrimEnd('?');

        // Allow built-in C# type aliases
        var builtInTypes = new HashSet<string>
        {
            "bool", "byte", "sbyte", "char", "decimal", "double", "float",
            "int", "uint", "long", "ulong", "short", "ushort", "string",
            "object", "nint", "nuint"
        };

        if (builtInTypes.Contains(baseType))
            return true;

        // Allow common .NET types
        var commonTypes = new HashSet<string>
        {
            "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan"
        };

        if (commonTypes.Contains(baseType))
            return true;

        // For custom types, validate as C# identifier (handles generic types with dots)
        // Split on '.' for fully qualified names and validate each part
        var parts = baseType.Split('.');
        return parts.All(CSharpIdentifierValidator.IsValid);
    }
}
