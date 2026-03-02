namespace Modulus.Templates;

/// <summary>
/// Represents a single rendered template file ready to be written to disk.
/// </summary>
/// <param name="RelativePath">The relative path from the output root where this file should be written.</param>
/// <param name="Content">The rendered file content with all tokens replaced.</param>
public sealed record TemplateOutput(string RelativePath, string Content);
