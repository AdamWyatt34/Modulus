namespace Modulus.Cli.Infrastructure;

public interface IProcessRunner
{
    Task<int> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
