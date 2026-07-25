using System.Xml;
using System.Xml.Linq;

namespace Modulus.Cli.Infrastructure;

/// <summary>
/// Shared, idempotent <c>ProjectReference</c> editing for csproj files. Detection always
/// inspects parsed XML — never raw text — so a comment or unrelated text mentioning a project's
/// file name never causes a required edit to be silently skipped (add), and a real reference
/// spelled with an unusual relative path is never missed (remove). The edit itself is a
/// targeted, minimal text change so unrelated formatting and comments in the file survive
/// untouched. This is the pattern <c>AddConsumerHandler</c> established for wiring the
/// module-to-Integration cross-module reference; <c>AddModuleHandler</c> and
/// <c>RemoveModuleHandler</c> reuse it for the host-to-module wiring.
/// </summary>
public static class ProjectReferenceEditor
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="csprojXml"/> declares a real <c>ProjectReference</c>
    /// whose <c>Include</c> resolves to <paramref name="expectedCsprojFileName"/>. Parses the XML
    /// so comments and unrelated nodes are ignored, and matches on the file name so it's
    /// independent of how the relative path is spelled and which separator it uses.
    /// </summary>
    public static bool HasReferenceTo(string csprojXml, string expectedCsprojFileName)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(csprojXml);
        }
        catch (XmlException)
        {
            // Callers validate well-formedness up front where it matters; treat an unparseable
            // file as "reference not present" so wiring is attempted rather than silently skipped.
            return false;
        }

        return document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Any(include => string.Equals(FileNameOf(include!), expectedCsprojFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a <c>&lt;ProjectReference Include="<paramref name="relativeReference"/>" /&gt;</c> in
    /// a new <c>ItemGroup</c> immediately before the closing <c>&lt;/Project&gt;</c> tag. Callers
    /// must have already verified the reference is not present (<see cref="HasReferenceTo"/>) and
    /// that the content contains a closing <c>&lt;/Project&gt;</c> tag.
    /// </summary>
    public static string AddReference(string csprojContent, string relativeReference)
    {
        var itemGroup =
            "  <ItemGroup>\n" +
            $"    <ProjectReference Include=\"{relativeReference}\" />\n" +
            "  </ItemGroup>\n\n";

        return csprojContent.Replace("</Project>", itemGroup + "</Project>");
    }

    /// <summary>
    /// Removes every line declaring a <c>ProjectReference</c> to <paramref name="expectedCsprojFileName"/>.
    /// Presence (and the <paramref name="removed"/> flag) is decided the same way as
    /// <see cref="HasReferenceTo"/> — via parsed XML — so a reference is reliably found
    /// regardless of the relative path depth used to spell it; the file name always appears
    /// as a substring of that line, so the line-scoped removal below matches it regardless of
    /// spelling. Returns the content unchanged when no matching reference exists.
    /// </summary>
    public static string RemoveReference(string csprojContent, string expectedCsprojFileName, out bool removed)
    {
        removed = HasReferenceTo(csprojContent, expectedCsprojFileName);
        if (!removed)
        {
            return csprojContent;
        }

        var lines = csprojContent.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        lines.RemoveAll(l => l.Contains("ProjectReference", StringComparison.Ordinal)
            && l.Contains(expectedCsprojFileName, StringComparison.OrdinalIgnoreCase));

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Extracts the final path segment, treating both <c>/</c> and <c>\</c> as separators so the
    /// comparison is correct regardless of the OS the CLI runs on.
    /// </summary>
    public static string FileNameOf(string path)
    {
        var normalized = path.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }
}
