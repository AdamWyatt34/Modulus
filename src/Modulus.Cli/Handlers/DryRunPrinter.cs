using Modulus.Cli.Infrastructure;

namespace Modulus.Cli.Handlers;

/// <summary>
/// Shared <c>--dry-run</c> output formatting for the scaffold handlers that only ever create new
/// files (add-entity, add-command, add-query, add-event): print exactly which files would be
/// created, with no other side effects to describe. Handlers with additional side effects
/// (process invocations, existing-file edits — init, add-module, add-endpoint, add-consumer)
/// print their own richer plan instead of using this helper.
/// </summary>
internal static class DryRunPrinter
{
    public static int PrintFileList(IConsoleOutput console, string header, IEnumerable<string> fullPaths)
    {
        console.WriteLine(header);

        foreach (var path in fullPaths)
        {
            console.WriteLine($"  create  {path}");
        }

        console.WriteLine("Re-run without --dry-run to apply.");
        return 0;
    }
}
