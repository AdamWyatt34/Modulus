namespace Modulus.Cli.Infrastructure;

public interface IConsoleOutput
{
    void WriteLine(string message);
    void WriteError(string message);
    void WriteSuccess(string message);
}
