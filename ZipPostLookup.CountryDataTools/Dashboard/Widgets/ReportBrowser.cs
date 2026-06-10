using Spectre.Console;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;

namespace ZipPostLookup.CountryDataTools.Dashboard.Widgets;

/// <summary>
/// Shared ← → page browser for per-country result reports. Extracted from the near-identical
/// loops in IntegrityDashboard, DbIntegrityDashboard, and SnapshotDashboard. For the current
/// page it renders the header (breadcrumb + status), a prev/next nav bar, then the caller's
/// body; ← / → move between pages and Esc returns.
/// </summary>
internal static class ReportBrowser
{
    /// <param name="country">Country label for a page (used in header + prev/next bar).</param>
    /// <param name="statusLabel">Full status markup for a page, e.g. "[green]✓ passed[/]".</param>
    /// <param name="breadcrumbPrefix">Header prefix, e.g. "Integrity › ZPL Data" or "Snapshot".</param>
    /// <param name="renderBody">Renders the current page's body (summary table, report path, …).</param>
    public static void Browse<T>(
        IReadOnlyList<T> pages,
        Func<T, string> country,
        Func<T, string> statusLabel,
        string breadcrumbPrefix,
        Action<T> renderBody)
    {
        if (pages.Count == 0) return;

        var index = 0;
        while (true)
        {
            var page      = pages[index];
            var prevLabel = index > 0               ? $"[bold]← {country(pages[index - 1])}[/]" : "[grey]←[/]";
            var nextLabel = index < pages.Count - 1 ? $"[bold]{country(pages[index + 1])} →[/]" : "[grey]→[/]";

            HeaderBar.Render($"{breadcrumbPrefix} › {country(page)}  {statusLabel(page)}");
            CdtCommandMenu.Render($"  {prevLabel}    ({index + 1}/{pages.Count})    {nextLabel}    [grey]Esc: back[/]");
            AnsiConsole.WriteLine();

            renderBody(page);

            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.LeftArrow  when index > 0:               index--; break;
                case ConsoleKey.RightArrow when index < pages.Count - 1: index++; break;
                case ConsoleKey.Escape:                                   return;
            }
        }
    }
}
