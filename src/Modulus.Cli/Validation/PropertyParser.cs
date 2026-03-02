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

            results.Add(new EntityProperty(name, type));
        }

        return (results, null);
    }
}
