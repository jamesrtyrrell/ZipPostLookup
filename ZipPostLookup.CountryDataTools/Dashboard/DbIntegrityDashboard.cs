using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using CdtDbIntegrity = ZipPostLookup.CountryDataTools.Commands.Handlers.CdtDbIntegrityCommand;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class DbIntegrityDashboard
{
    private sealed record ReportPage(
        string Country, string? ReportPath, int ExitCode,
        CdtDbIntegrity.DbCheckResults? CheckResults);

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Integrity › CDT DB");

            WorkDbContext? db;
            try
            {
                db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]  ✗ DB connection failed: {Markup.Escape(ex.Message)}[/]");
                FooterBar.PressAnyKey();
                Console.ReadKey(intercept: true);
                return 1;
            }

            var country = CdtSelectMenu.Show(
                ["US", "CA", "MX", "All (US + CA + MX)", "← Back"],
                s => s switch
                {
                    "← Back"             => "[grey]← Back[/]",
                    "All (US + CA + MX)" => $"[bold cyan]{"All",-14}[/]  [grey]Run all three, browse results with ← →[/]",
                    _                    => $"[bold cyan]{s,-14}[/]",
                },
                escapeReturns: "← Back",
                title: "Select country:");

            if (country == "← Back") break;

            if (country.StartsWith("All"))
            {
                HeaderBar.Render("Integrity › CDT DB › All");
                AnsiConsole.WriteLine();

                var pages = new List<ReportPage>();

                foreach (var cc in new[] { "US", "CA", "MX" })
                {
                    HeaderBar.Render($"Integrity › CDT DB › {cc}");

                    var (exitCode, checkResults, reportPath) =
                        await CdtDbIntegrity.RunForCountryAsync(db, cc.ToUpperInvariant(), null);

                    pages.Add(new ReportPage(cc, File.Exists(reportPath) ? reportPath : null, exitCode, checkResults));

                    if (cc != "MX")
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine("[grey]  Next country in 2 s...[/]");
                        await Task.Delay(2_000);
                    }
                }

                BrowseReports(pages);
            }
            else
            {
                HeaderBar.Render($"Integrity › CDT DB › {country}");
                AnsiConsole.WriteLine();

                var (exitCode, checkResults, reportPath) =
                    await CdtDbIntegrity.RunForCountryAsync(db, country.ToUpperInvariant(), null);

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(exitCode == 0
                    ? "[green]  ✓ No issues found.[/]"
                    : "[red]  ✗ Issues detected — see report for details.[/]");
                AnsiConsole.WriteLine();
                if (File.Exists(reportPath))
                    AnsiConsole.MarkupLine($"  [grey]Report: {Markup.Escape(reportPath)}[/]");
                AnsiConsole.WriteLine();
                FooterBar.PressAnyKey();
                Console.ReadKey(intercept: true);
            }
        }

        return 0;
    }

    // ── Results browser (← → navigation across countries) ────────────────────

    private static void BrowseReports(List<ReportPage> pages)
    {
        var index = 0;

        while (true)
        {
            var page      = pages[index];
            var prevLabel = index > 0               ? $"[bold]← {pages[index - 1].Country}[/]" : "[grey]←[/]";
            var nextLabel = index < pages.Count - 1 ? $"[bold]{pages[index + 1].Country} →[/]" : "[grey]→[/]";
            var status    = page.ExitCode == 0      ? "[green]✓ passed[/]"                      : "[red]✗ issues[/]";

            HeaderBar.Render($"Integrity › CDT DB › {page.Country}  {status}");
            CdtCommandMenu.Render($"  {prevLabel}    ({index + 1}/{pages.Count})    {nextLabel}    [grey]Esc: back[/]");
            AnsiConsole.WriteLine();

            if (page.CheckResults is not null)
                CdtDbIntegrity.PrintSummaryTable(page.Country, page.CheckResults);
            else
                AnsiConsole.MarkupLine("[grey]  No results available.[/]");

            if (page.ReportPath is not null)
                AnsiConsole.MarkupLine($"  [grey]Full report: {Markup.Escape(page.ReportPath)}[/]");

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
