namespace Modulus.Cli.IntegrationTests;

/// <summary>
/// Points scaffolded solutions at a locally packed ModulusKit feed so E2E tests exercise
/// HEAD packages instead of nuget.org. Controlled by two environment variables:
/// <c>MODULUS_E2E_FEED</c> (path to a folder of .nupkg files, produced by
/// <c>scripts/New-E2EFeed.ps1</c> or the CI pack step) and
/// <c>MODULUS_E2E_PACKAGE_VERSION</c> (the version those packages were packed as).
/// When either is unset the tests fall back to nuget.org and the CLI's own version,
/// which is the post-publish smoke path.
/// </summary>
internal static class E2EPackageFeed
{
    /// <summary>
    /// If a local feed is configured, writes a nuget.config into <paramref name="workspaceRoot"/>
    /// (NuGet config discovery walks up from the scaffolded solution, so this governs its restore)
    /// and returns the package version to pass as <c>modulusKitVersion</c>. Returns null when no
    /// feed is configured.
    /// </summary>
    public static string? Configure(string workspaceRoot)
    {
        var feed = Environment.GetEnvironmentVariable("MODULUS_E2E_FEED");
        var version = Environment.GetEnvironmentVariable("MODULUS_E2E_PACKAGE_VERSION");

        if (string.IsNullOrWhiteSpace(feed) || string.IsNullOrWhiteSpace(version))
            return null;

        // <clear /> isolates the scaffold from user/machine-level package sources so the
        // test resolves ModulusKit.* deterministically from the local feed.
        var nugetConfig = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local-e2e" value="{feed}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """;

        File.WriteAllText(Path.Combine(workspaceRoot, "nuget.config"), nugetConfig);
        return version;
    }
}
