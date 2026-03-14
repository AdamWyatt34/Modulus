using System.Reflection;

namespace Modulus.Templates;

/// <summary>
/// Options for scaffolding a new solution via <c>modulus init</c>.
/// </summary>
public sealed record InitOptions
{
    /// <summary>
    /// The PascalCase name of the solution (e.g. "EShop").
    /// </summary>
    public required string SolutionName { get; init; }

    /// <summary>
    /// Whether to include .NET Aspire orchestration projects.
    /// </summary>
    public bool IncludeAspire { get; init; }

    /// <summary>
    /// The version of ModulusKit packages to reference in Directory.Packages.props.
    /// Defaults to the CLI's own assembly version so scaffolded solutions always match the installed CLI.
    /// </summary>
    public string ModulusKitVersion { get; init; } = ResolveDefaultVersion();

    private static string ResolveDefaultVersion()
    {
        var version = typeof(InitOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        // Strip the +commitHash suffix if present (e.g. "1.2.0+abc123" → "1.2.0")
        if (version is not null)
        {
            var plusIndex = version.IndexOf('+');
            if (plusIndex >= 0)
            {
                version = version[..plusIndex];
            }
        }

        return version ?? "1.0.0";
    }
}
