using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Tests.Fakes;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<Invocation> _invocations = [];

    public IReadOnlyList<Invocation> Invocations => _invocations;

    public int ExitCodeToReturn { get; set; }

    public Task<int> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _invocations.Add(new Invocation(command, [.. arguments], workingDirectory));
        return Task.FromResult(ExitCodeToReturn);
    }

    public sealed record Invocation(string Command, IReadOnlyList<string> Arguments, string WorkingDirectory);
}
