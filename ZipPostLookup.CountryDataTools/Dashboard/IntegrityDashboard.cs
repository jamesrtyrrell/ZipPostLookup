using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Commands;
using ZplIntegrity = ZipPostLookup.CountryDataTools.Commands.Handlers.IntegrityCheckCommand;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class IntegrityDashboard
{
    private sealed record ReportPage(
        string Country, string? ReportPath, int ExitCode,
        IntegrityCheckSummary? ZplSummary = null);

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Integrity › ZPL Data");

            var country = CountryPicker.Show(
                title: "Select country:",
                cancelLabel: "← Back",
                allLabel: "All (US + CA + MX)",
                allDescription: "Run all three, browse results with ← →");

            if (country == "← Back") break;

            await RunZplModeAsync(country);
        }

        return 0;
    }

    // ── ZPL Data mode ─────────────────────────────────────────────────────────

    private static async Task RunZplModeAsync(string country)
    {
        WorkDbContext? db = null;
        try { db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory()); }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  ✗ DB connection failed: {Markup.Escape(ex.Message)}[/]");
            FooterBar.PressAnyKey();
            Console.ReadKey(intercept: true);
            return;
        }

        var tests = PromptTests();
        AnsiConsole.WriteLine();

        if (country.StartsWith("All"))
        {
            HeaderBar.Render("Integrity › ZPL Data › All");
            await RunZplAllAndBrowseAsync(db, ["US", "CA", "MX"], tests);
        }
        else
        {
            HeaderBar.Render($"Integrity › ZPL Data › {country}");

            var (exitCode, summary, reportPath) =
                await ZplIntegrity.RunForCountryAsync(db, country.ToUpperInvariant(), tests, null);

            AnsiConsole.WriteLine();
            ShowResultBanner(exitCode);
            if (reportPath is not null) ShowReportPath(reportPath);
            FooterBar.PressAnyKey();
            Console.ReadKey(intercept: true);
        }
    }

    private static async Task RunZplAllAndBrowseAsync(WorkDbContext db, string[] countries, int tests)
    {
        var pages = new List<ReportPage>();

        foreach (var cc in countries)
        {
            HeaderBar.Render($"Integrity › ZPL Data › {cc}");

            var (exitCode, summary, reportPath) =
                await ZplIntegrity.RunForCountryAsync(db, cc.ToUpperInvariant(), tests, null);

            pages.Add(new ReportPage(cc,
                reportPath is not null && File.Exists(reportPath) ? reportPath : null,
                exitCode, ZplSummary: summary));

            if (cc != countries[^1])
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Next country in 2 s...[/]");
                await Task.Delay(2_000);
            }
        }

        BrowseReports(pages);
    }

    // ── Results browser (← → navigation across countries) ────────────────────

    private static void BrowseReports(List<ReportPage> pages) =>
        ReportBrowser.Browse(
            pages,
            p => p.Country,
            p => p.ExitCode == 0 ? "[green]✓ passed[/]" : "[red]✗ issues[/]",
            "Integrity › ZPL Data",
            p =>
            {
                if (p.ZplSummary is not null)
                    IntegrityDisplay.PrintSummary(p.Country, p.ZplSummary);
                else
                    AnsiConsole.MarkupLine("[grey]  No results available.[/]");

                if (p.ReportPath is not null)
                    AnsiConsole.MarkupLine($"  [grey]Full report: {Markup.Escape(p.ReportPath)}[/]");
            });

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int PromptTests() =>
        AnsiConsole.Prompt(
            new TextPrompt<int>("  Tests [grey](default 1000)[/]:")
                .DefaultValue(1000)
                .Validate(n => n > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Enter a positive number[/]")));

    private static void ShowResultBanner(int exitCode)
    {
        AnsiConsole.MarkupLine(exitCode == 0
            ? "[green]  ✓ No issues found.[/]"
            : "[red]  ✗ Issues detected — see report for details.[/]");
        AnsiConsole.WriteLine();
    }

    private static void ShowReportPath(string path)
    {
        if (File.Exists(path))
            AnsiConsole.MarkupLine($"  [grey]Report: {Markup.Escape(path)}[/]");
        AnsiConsole.WriteLine();
    }
}
