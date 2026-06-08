using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Dashboard.Widgets;

/// <summary>
/// Renders the horizontal keyboard-shortcut hint bar at the bottom of a screen.
/// Stage 2 hook: swap the body to use Spectre Layout for a fixed bottom panel.
/// </summary>
internal static class CdtCommandMenu
{
    public static void Render(string markupHints) =>
        AnsiConsole.MarkupLine(markupHints);
}
