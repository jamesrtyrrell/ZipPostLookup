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
}
