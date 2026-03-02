namespace Modulus.Cli.Infrastructure;

public sealed class ConsoleOutput : IConsoleOutput
{
    public void WriteLine(string message) => Console.WriteLine(message);

    public void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }

    public void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
