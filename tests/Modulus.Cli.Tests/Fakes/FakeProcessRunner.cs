using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Tests.Fakes;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<Invocation> _invocations = [];

    public IReadOnlyList<Invocation> Invocations => _invocations;

    public int ExitCodeToReturn { get; set; }

    /// <summary>
    /// Optional per-invocation exit code override, keyed by the space-joined
    /// "command arg1 arg2 ..." text of the call (e.g. <c>"git add ."</c>). Checked before falling
    /// back to <see cref="ExitCodeToReturn"/>, so a single test can simulate one command
    /// succeeding while a later one in the same handler run fails.
    /// </summary>
    public Dictionary<string, int> ExitCodeOverrides { get; } = new(StringComparer.Ordinal);

    public Task<int> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _invocations.Add(new Invocation(command, [.. arguments], workingDirectory));

        var key = string.Join(' ', new[] { command }.Concat(arguments));
        return Task.FromResult(ExitCodeOverrides.TryGetValue(key, out var exitCode) ? exitCode : ExitCodeToReturn);
    }

    public sealed record Invocation(string Command, IReadOnlyList<string> Arguments, string WorkingDirectory);
}
