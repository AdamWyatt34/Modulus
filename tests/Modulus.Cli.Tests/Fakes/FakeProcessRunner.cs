using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Tests.Fakes;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(string Command, string Arguments, string WorkingDirectory)> _invocations = [];

    public IReadOnlyList<(string Command, string Arguments, string WorkingDirectory)> Invocations => _invocations;

    public int ExitCodeToReturn { get; set; }

    public Task<int> RunAsync(string command, string arguments, string workingDirectory)
    {
        _invocations.Add((command, arguments, workingDirectory));
        return Task.FromResult(ExitCodeToReturn);
    }
}
