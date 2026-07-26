using System.Text.RegularExpressions;

namespace Modulus.Templates;

/// <summary>
/// A single ASP.NET Core minimal API route parameter extracted from a route template — e.g.
/// <c>Name="itemId", ClrType="Guid"</c> from the segment <c>{itemId:guid}</c>.
/// </summary>
public sealed record RouteParameter(string Name, string ClrType);

/// <summary>
/// Parses ASP.NET Core minimal API route templates (e.g. <c>/items/{itemId:int}</c>) into their
/// constituent route parameters, for binding into the generated endpoint's lambda signature and
/// the wired command/query's constructor call. Supports the syntax
/// <c>AddEndpointHandler</c>'s route-validation regex already permits: a bare name
/// (<c>{id}</c>), a type constraint (<c>{id:guid}</c>), an optional marker (<c>{id:int?}</c> or
/// <c>{id?}</c>), and a default-value clause (<c>{id=1}</c>) — the default-value clause is
/// recognized (and stripped from the parameter name) but not otherwise acted on, since minimal
/// API route defaults require no special handling in the delegate signature itself.
/// </summary>
public static class RouteTemplateParser
{
    private static readonly Regex BraceSegment = new(@"\{([^{}]*)\}", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> ConstraintToClrType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int"] = "int",
        ["long"] = "long",
        ["guid"] = "Guid",
        ["bool"] = "bool",
        ["decimal"] = "decimal",
        ["double"] = "double",
        ["float"] = "float",
        ["datetime"] = "DateTime",
    };

    /// <summary>
    /// Extracts every route parameter from <paramref name="route"/>, in the order it appears.
    /// A segment with an empty name (e.g. a stray <c>{}</c>) is skipped.
    /// </summary>
    public static IReadOnlyList<RouteParameter> Parse(string route)
    {
        var results = new List<RouteParameter>();

        foreach (Match match in BraceSegment.Matches(route))
        {
            var (name, constraint, optional) = ExtractParts(match.Groups[1].Value);
            if (name.Length == 0)
            {
                continue;
            }

            var clrType = MapConstraint(constraint);
            if (optional)
            {
                clrType = clrType == "string" ? "string?" : clrType + "?";
            }

            results.Add(new RouteParameter(name, clrType));
        }

        return results;
    }

    /// <summary>
    /// Rewrites a route template so every parameter segment becomes a bare <c>{name}</c>
    /// interpolation hole, stripping any <c>:constraint</c>, <c>?</c>, or <c>=default</c> clause.
    /// This is what makes it safe to splice the route into a generated C# interpolated string:
    /// a raw <c>{id:guid}</c> segment would otherwise be parsed by the *generated* code as an
    /// interpolation alignment/format-string clause (<c>id</c> formatted with the non-existent
    /// <c>"guid"</c> format), which throws <see cref="FormatException"/> at runtime even though
    /// it compiles.
    /// </summary>
    public static string ToInterpolationTemplate(string route)
    {
        return BraceSegment.Replace(route, match =>
        {
            var (name, _, _) = ExtractParts(match.Groups[1].Value);
            return name.Length == 0 ? match.Value : "{" + name + "}";
        });
    }

    private static (string Name, string? Constraint, bool Optional) ExtractParts(string raw)
    {
        var name = raw;
        var optional = false;
        string? constraint = null;

        var equalsIndex = name.IndexOf('=');
        if (equalsIndex >= 0)
        {
            name = name[..equalsIndex];
        }

        if (name.EndsWith('?'))
        {
            optional = true;
            name = name[..^1];
        }

        var colonIndex = name.IndexOf(':');
        if (colonIndex >= 0)
        {
            constraint = name[(colonIndex + 1)..];
            name = name[..colonIndex];

            if (constraint.EndsWith('?'))
            {
                optional = true;
                constraint = constraint[..^1];
            }
        }

        return (name, constraint, optional);
    }

    private static string MapConstraint(string? constraint) =>
        constraint is not null && ConstraintToClrType.TryGetValue(constraint, out var clrType)
            ? clrType
            : "string";
}
