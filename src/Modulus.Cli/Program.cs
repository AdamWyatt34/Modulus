using System.CommandLine;
using Modulus.Cli.Commands;
using Modulus.Cli.Infrastructure;

var fileSystem = new FileSystem();
var processRunner = new ProcessRunner();
var consoleOutput = new ConsoleOutput();

var rootCommand = new RootCommand("Modulus - Modular Monolith CLI scaffolding tool");

rootCommand.Subcommands.Add(InitCommand.Create(fileSystem, processRunner, consoleOutput));
rootCommand.Subcommands.Add(AddModuleCommand.Create(fileSystem, processRunner, consoleOutput));
rootCommand.Subcommands.Add(AddEntityCommand.Create(fileSystem, consoleOutput));
rootCommand.Subcommands.Add(AddCommandCommand.Create(fileSystem, consoleOutput));
rootCommand.Subcommands.Add(AddQueryCommand.Create(fileSystem, consoleOutput));
rootCommand.Subcommands.Add(AddEndpointCommand.Create(fileSystem, consoleOutput));
rootCommand.Subcommands.Add(ListModulesCommand.Create(fileSystem, consoleOutput));
rootCommand.Subcommands.Add(VersionCommand.Create(consoleOutput));

return await rootCommand.Parse(args).InvokeAsync();
