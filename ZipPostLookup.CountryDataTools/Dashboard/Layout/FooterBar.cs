using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Dashboard.Layout;

/// <summary>
/// Renders the footer hint line at the bottom of a screen.
/// Stage 2 hook: swap to a Spectre Layout fixed-bottom panel.
/// </summary>
internal static class FooterBar
{
    public static void PressAnyKey() =>
        AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");

    /// <summary>
    /// Prints a blank line, a ✓/✗ result banner, then the press-any-key prompt and waits for a key.
    /// The common tail of every dashboard action that shells out to a command.
    /// </summary>
    /// <param name="exitCode">Process exit code — 0 renders the green success banner.</param>
    /// <param name="okMessage">Success text after the ✓ (default "Done").</param>
    /// <param name="failMessage">Failure text after the ✗ (default "Exited with code {exitCode}").</param>
    public static void ShowResultAndPause(int exitCode, string okMessage = "Done", string? failMessage = null)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(exitCode == 0
            ? $"[green]  ✓ {okMessage}[/]"
            : $"[red]  ✗ {failMessage ?? $"Exited with code {exitCode}"}[/]");
        AnsiConsole.WriteLine();
        PressAnyKey();
        Console.ReadKey(intercept: true);
    }
}
