using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Tests.Fakes;

public sealed class FakeConsole : IConsoleOutput
{
    public List<string> Lines { get; } = [];
    public List<string> ErrorLines { get; } = [];
    public List<string> SuccessLines { get; } = [];

    public void WriteLine(string message) => Lines.Add(message);
    public void WriteError(string message) => ErrorLines.Add(message);
    public void WriteSuccess(string message) => SuccessLines.Add(message);
}
