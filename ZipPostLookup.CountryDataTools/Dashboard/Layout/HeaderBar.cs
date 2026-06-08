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

        var status = TryReadDbStatus();
        if (status is not null)
            AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(status)}[/]");

        AnsiConsole.WriteLine();
    }

    private static string? TryReadDbStatus()
    {
        try
        {
            var configPath = WorkDbConfig.FindConfigFile(Directory.GetCurrentDirectory());
            if (configPath is null) return null;

            var config = WorkDbConfig.Load(configPath);
            var cc     = config.CountryCode.ToUpperInvariant();
            var run    = string.IsNullOrWhiteSpace(config.ActiveRunId) ? "(no run)" : config.ActiveRunId;
            return $"CC: {cc}  ·  Run: {run}";
        }
        catch
        {
            return null;
        }
    }
}
