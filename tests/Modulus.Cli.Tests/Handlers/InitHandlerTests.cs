using Modulus.Cli.Handlers;
using Modulus.Cli.Tests.Fakes;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Handlers;

public class InitHandlerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeProcessRunner _proc = new();
    private readonly FakeConsole _console = new();

    private InitHandler CreateHandler() => new(_fs, _proc, _console);

    [Fact]
    public async Task Init_creates_all_expected_files()
    {
        _fs.SetCurrentDirectory(@"C:\work");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: false, "inmemory", noGit: true);

        result.ShouldBe(0);

        // Verify key files were created
        _fs.FileExists(@"C:\work\EShop\Directory.Build.props").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\Directory.Packages.props").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\.editorconfig").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\.gitignore").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\EShop.slnx").ShouldBeTrue();

        // BuildingBlocks
        _fs.FileExists(@"C:\work\EShop\src\BuildingBlocks.Domain\BuildingBlocks.Domain.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\BuildingBlocks.Application\BuildingBlocks.Application.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\BuildingBlocks.Infrastructure\BuildingBlocks.Infrastructure.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\BuildingBlocks.Integration\BuildingBlocks.Integration.csproj").ShouldBeTrue();

        // WebApi / Host
        _fs.FileExists(@"C:\work\EShop\src\EShop.WebApi\EShop.WebApi.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\EShop.WebApi\Program.cs").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\src\EShop.WebApi\appsettings.json").ShouldBeTrue();

        // Tests
        _fs.FileExists(@"C:\work\EShop\tests\EShop.Tests.Common\EShop.Tests.Common.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\tests\EShop.Tests.Architecture\EShop.Tests.Architecture.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\tests\EShop.Tests.Integration\EShop.Tests.Integration.csproj").ShouldBeTrue();
    }

    [Fact]
    public async Task Init_with_aspire_includes_aspire_projects()
    {
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: true, "inmemory", noGit: true);

        result.ShouldBe(0);
        _fs.FileExists(@"C:\work\EShop\aspire\EShop.AppHost\EShop.AppHost.csproj").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\aspire\EShop.AppHost\Program.cs").ShouldBeTrue();
        _fs.FileExists(@"C:\work\EShop\aspire\EShop.ServiceDefaults\EShop.ServiceDefaults.csproj").ShouldBeTrue();

        // Verify .slnx includes aspire entries
        var slnxContent = _fs.ReadAllText(@"C:\work\EShop\EShop.slnx");
        slnxContent.ShouldContain("AppHost");
        slnxContent.ShouldContain("ServiceDefaults");
    }

    [Fact]
    public async Task Init_without_aspire_excludes_aspire_projects()
    {
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: false, "inmemory", noGit: true);

        result.ShouldBe(0);

        var hasAspire = _fs.AllFiles.Keys.Any(k => k.Contains("AppHost", StringComparison.OrdinalIgnoreCase));
        hasAspire.ShouldBeFalse();
    }

    [Fact]
    public async Task Init_with_rabbitmq_transport_adds_messaging_config()
    {
        var handler = CreateHandler();

        await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: false, "rabbitmq", noGit: true);

        var appSettings = _fs.ReadAllText(@"C:\work\EShop\src\EShop.WebApi\appsettings.json");
        appSettings.ShouldContain("Messaging");
        appSettings.ShouldContain("RabbitMq");
        appSettings.ShouldContain("ConnectionString");
    }

    [Fact]
    public async Task Init_with_inmemory_transport_omits_messaging_connection_string()
    {
        var handler = CreateHandler();

        await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: false, "inmemory", noGit: true);

        var appSettings = _fs.ReadAllText(@"C:\work\EShop\src\EShop.WebApi\appsettings.json");
        appSettings.ShouldContain("InMemory");
        // The Messaging section should not have a ConnectionString (but the DB ConnectionStrings section is fine)
        var messagingIndex = appSettings.IndexOf("Messaging", StringComparison.Ordinal);
        var messagingSection = appSettings[messagingIndex..];
        messagingSection.ShouldNotContain("ConnectionString");
    }

    [Fact]
    public async Task Init_runs_dotnet_restore()
    {
        var handler = CreateHandler();

        await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: false, "inmemory", noGit: true);

        _proc.Invocations.ShouldContain(i => i.Command == "dotnet" && i.Arguments == "restore");
    }

    [Fact]
    public async Task Init_runs_git_init_and_commit_by_default()
    {
        var handler = CreateHandler();

        await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: false, "inmemory", noGit: false);

        _proc.Invocations.ShouldContain(i => i.Command == "git" && i.Arguments == "init");
        _proc.Invocations.ShouldContain(i => i.Command == "git" && i.Arguments == "add .");
        _proc.Invocations.ShouldContain(i => i.Command == "git" && i.Arguments.Contains("commit"));
    }

    [Fact]
    public async Task Init_with_no_git_skips_git_commands()
    {
        var handler = CreateHandler();

        await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: false, "inmemory", noGit: true);

        _proc.Invocations.ShouldNotContain(i => i.Command == "git");
    }

    [Fact]
    public async Task Init_with_invalid_name_returns_error()
    {
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("123Bad", @"C:\work", includeAspire: false, "inmemory", noGit: true);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("123Bad"));
    }

    [Fact]
    public async Task Init_into_existing_nonempty_directory_returns_error()
    {
        _fs.SeedFile(@"C:\work\EShop\existing.txt", "content");
        var handler = CreateHandler();

        var result = await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: false, "inmemory", noGit: true);

        result.ShouldBe(1);
        _console.ErrorLines.ShouldContain(l => l.Contains("already exists"));
    }

    [Fact]
    public async Task Init_with_azureservicebus_transport_adds_connection_string()
    {
        var handler = CreateHandler();

        await handler.ExecuteAsync("EShop", @"C:\work", includeAspire: false, "azureservicebus", noGit: true);

        var appSettings = _fs.ReadAllText(@"C:\work\EShop\src\EShop.WebApi\appsettings.json");
        appSettings.ShouldContain("AzureServiceBus");
        appSettings.ShouldContain("ConnectionString");
    }
}
