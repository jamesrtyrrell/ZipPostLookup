using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class DashboardRenderer
{
    private const string AppTitle = "ZipPostLookup";

    public static void RenderHeader(string pageTitle)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($" [bold cyan]{AppTitle}[/]  [grey]›[/]  [bold]{pageTitle}[/]");
        AnsiConsole.Write(new Rule().LeftJustified());

        // Dim status line from workdb.json — file read only, no DB connection.
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
            var run    = string.IsNullOrWhiteSpace(config.ActiveRunId)
                ? "(no run)"
                : config.ActiveRunId;

            return $"CC: {cc}  ·  Run: {run}";
        }
        catch
        {
            return null;
        }
    }
}
