using Modulus.Cli.Handlers;
using Modulus.Cli.Infrastructure;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class UpgradeHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeConsole _console = new();

    private const string Slnx = @"C:\work\EShop\EShop.slnx";
    private const string SolutionRoot = @"C:\work\EShop";
    private static readonly string PropsPath = Path.Combine(SolutionRoot, "Directory.Packages.props");

    // Deliberately messy: comments, a non-ModulusKit pin sharing the ModulusKit version
    // literal, and mixed indentation — all of it must survive an upgrade byte-for-byte
    // outside the rewritten version substrings.
    private const string Props =
        "<Project>\n" +
        "  <!-- central pins -->\n" +
        "  <ItemGroup>\n" +
        "    <PackageVersion Include=\"ModulusKit.Mediator\" Version=\"1.2.5\" />\n" +
        "    <PackageVersion Include=\"ModulusKit.Messaging\" Version=\"1.2.5\" />\n" +
        "      <PackageVersion Include=\"ModulusKit.Analyzers\" Version=\"1.2.4\" />\n" +
        "    <PackageVersion Include=\"SomeOther.Package\" Version=\"1.2.5\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    private UpgradeHandler CreateHandler()
        => new(_fs, _console, new SolutionFinder(_fs));

    private void Seed(string props = Props)
    {
        _fs.SetCurrentDirectory(SolutionRoot);
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        _fs.SeedFile(Path.Combine(SolutionRoot, "src", "EShop.WebApi", "Program.cs"), "// program");
        _fs.SeedFile(PropsPath, props);
    }

    [Fact]
    public async Task Upgrade_bumps_only_moduluskit_pins()
    {
        Seed();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("2.0.0", Slnx, dryRun: false);

        exit.ShouldBe(0);
        var written = _fs.ReadAllText(PropsPath);
        written.ShouldContain("<PackageVersion Include=\"ModulusKit.Mediator\" Version=\"2.0.0\" />");
        written.ShouldContain("<PackageVersion Include=\"ModulusKit.Messaging\" Version=\"2.0.0\" />");
        written.ShouldContain("<PackageVersion Include=\"ModulusKit.Analyzers\" Version=\"2.0.0\" />");
        written.ShouldContain("<PackageVersion Include=\"SomeOther.Package\" Version=\"1.2.5\" />");
    }

    [Fact]
    public async Task Upgrade_preserves_formatting_and_comments()
    {
        Seed();
        var handler = CreateHandler();

        await handler.ExecuteAsync("2.0.0", Slnx, dryRun: false);

        // Everything except the three version substrings must be untouched.
        var expected = Props
            .Replace("\"ModulusKit.Mediator\" Version=\"1.2.5\"", "\"ModulusKit.Mediator\" Version=\"2.0.0\"")
            .Replace("\"ModulusKit.Messaging\" Version=\"1.2.5\"", "\"ModulusKit.Messaging\" Version=\"2.0.0\"")
            .Replace("\"ModulusKit.Analyzers\" Version=\"1.2.4\"", "\"ModulusKit.Analyzers\" Version=\"2.0.0\"");
        _fs.ReadAllText(PropsPath).ShouldBe(expected);
    }

    [Fact]
    public async Task Upgrade_dry_run_reports_but_does_not_write()
    {
        Seed();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("2.0.0", Slnx, dryRun: true);

        exit.ShouldBe(0);
        _fs.ReadAllText(PropsPath).ShouldBe(Props);
        _console.Lines.ShouldContain(l => l.Contains("Dry run"));
        _console.Lines.ShouldContain(l => l.Contains("ModulusKit.Mediator"));
    }

    [Fact]
    public async Task Upgrade_already_at_target_reports_no_changes()
    {
        Seed();
        var handler = CreateHandler();
        await handler.ExecuteAsync("2.0.0", Slnx, dryRun: false);
        _console.Lines.Clear();

        var exit = await handler.ExecuteAsync("2.0.0", Slnx, dryRun: false);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("already at the target version"));
    }

    [Fact]
    public async Task Upgrade_missing_props_file_fails()
    {
        _fs.SetCurrentDirectory(SolutionRoot);
        _fs.SeedFile(Slnx, "<Solution></Solution>");
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("2.0.0", Slnx, dryRun: false);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(e => e.Contains("Directory.Packages.props"));
    }

    [Fact]
    public async Task Upgrade_malformed_xml_fails()
    {
        Seed("<Project><ItemGroup>");
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("2.0.0", Slnx, dryRun: false);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(e => e.Contains("not well-formed"));
    }

    [Fact]
    public async Task Upgrade_no_moduluskit_pins_is_a_noop_success()
    {
        Seed("<Project>\n  <ItemGroup>\n    <PackageVersion Include=\"Other\" Version=\"1.0.0\" />\n  </ItemGroup>\n</Project>\n");
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("2.0.0", Slnx, dryRun: false);

        exit.ShouldBe(0);
        _console.Lines.ShouldContain(l => l.Contains("Nothing to upgrade"));
        _fs.ReadAllText(PropsPath).ShouldContain("Version=\"1.0.0\"");
    }

    [Fact]
    public async Task Upgrade_no_solution_found_fails()
    {
        _fs.SetCurrentDirectory(@"C:\elsewhere");
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("2.0.0", solutionPath: null, dryRun: false);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(e => e.Contains("Could not find a solution file"));
    }

    // ── --version validation ──────────────────────────────────────

    [Theory]
    [InlineData("2.0.0\" Foo=\"bar")]
    [InlineData("2.0.0$1")]
    [InlineData("$(EvilProp)")]
    [InlineData("not-a-version")]
    [InlineData("1")]
    public async Task Upgrade_rejects_invalid_version_and_writes_nothing(string badVersion)
    {
        Seed();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync(badVersion, Slnx, dryRun: false);

        exit.ShouldBe(1);
        _console.ErrorLines.ShouldContain(e => e.Contains("not a valid NuGet package version"));
        _fs.ReadAllText(PropsPath).ShouldBe(Props);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-preview.1")]
    [InlineData("1.2.3-alpha.0.5+abc123")]
    public async Task Upgrade_accepts_legitimate_version_shapes(string goodVersion)
    {
        Seed();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync(goodVersion, Slnx, dryRun: false);

        exit.ShouldBe(0);
        _fs.ReadAllText(PropsPath).ShouldContain($"Version=\"{goodVersion}\"");
    }

    [Fact]
    public async Task Upgrade_version_containing_dollar_sign_is_rejected_before_any_replace_runs()
    {
        // Regression: a literal "$" in the replacement value used to risk Regex.Replace
        // interpreting it as a backreference ($1, $$, ...) and corrupting the XML. The version
        // validator now rejects it outright, and the replacement additionally uses a
        // MatchEvaluator so the value is never parsed as a replacement pattern either way.
        Seed();
        var handler = CreateHandler();

        var exit = await handler.ExecuteAsync("2.0.0$$1", Slnx, dryRun: false);

        exit.ShouldBe(1);
        _fs.ReadAllText(PropsPath).ShouldBe(Props);
    }
}
