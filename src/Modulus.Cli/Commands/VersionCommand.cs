using System.CommandLine;
using System.Reflection;
using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Commands;

public static class VersionCommand
{
    public static Command Create(IConsoleOutput console)
    {
        var command = new Command("version", "Display the Modulus CLI version");

        command.SetAction(_ =>
        {
            var version = typeof(VersionCommand).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                ?? typeof(VersionCommand).Assembly.GetName().Version?.ToString()
                ?? "unknown";

            console.WriteLine($"modulus {version}");
        });

        return command;
    }
}
