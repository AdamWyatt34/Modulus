namespace Modulus.Cli.Infrastructure;

public interface IProcessRunner
{
    Task<int> RunAsync(string command, string arguments, string workingDirectory);
}
