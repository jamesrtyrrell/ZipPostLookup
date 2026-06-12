using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.CountryDataTools.Dashboard.Layout;

/// <summary>
/// Renders the fixed top region: title + breadcrumb + separator + workdb status.
/// Replaces DashboardRenderer.RenderHeader() — use HeaderBar.Render() at all call sites.
/// Stage 2 hook: replace AnsiConsole.Clear() + markup with a Spectre Layout top panel.
/// </summary>
internal static class HeaderBar
{
    public static void Render(string pageTitle)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine(TitleBar.Markup + BreadCrumbBar.Markup(pageTitle));
        AnsiConsole.Write(new Rule().LeftJustified());
        AnsiConsole.WriteLine();
    }


}
