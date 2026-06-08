using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Commands;
using ZplIntegrity    = ZipPostLookup.CountryDataTools.Commands.Handlers.IntegrityCheckCommand;
using CdtDbIntegrity  = ZipPostLookup.CountryDataTools.Commands.Handlers.CdtDbIntegrityCommand;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class IntegrityDashboard
{
    private sealed record ReportPage(
        string Country, string Mode, string? ReportPath, int ExitCode,
        CdtDbIntegrity.DbCheckResults? CheckResults = null,
        IntegrityCheckSummary? ZplSummary = null);

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Integrity");

            var mode = CdtSelectMenu.Show(
                ["ZPL Data", "CDT DB", "← Back"],
                s => s switch
                {
                    "← Back"   => "[grey]← Back[/]",
                    "ZPL Data" => $"[bold cyan]{"ZPL Data",-14}[/]  [grey]Compare library vs DB — detects stale exports[/]",
                    "CDT DB"   => $"[bold cyan]{"CDT DB",-14}[/]  [grey]Scan DB for admin errors, orphans, duplicates[/]",
                    _          => s,
                },
                escapeReturns: "← Back",
                title: "Integrity mode:");

            if (mode == "← Back") break;

            HeaderBar.Render($"Integrity › {mode}");

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

            if (country == "← Back") continue;

            if (mode == "ZPL Data")
                await RunZplModeAsync(country);
            else
                await RunCdtDbModeAsync(country);
        }

        return 0;
    }

    // ── ZPL Data mode (existing integrity check) ───────────────────────────────

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

            pages.Add(new ReportPage(cc, "ZPL Data",
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

    // ── CDT DB mode (new DB quality scan) ─────────────────────────────────────

    private static async Task RunCdtDbModeAsync(string country)
    {
        WorkDbContext? db = null;
        try
        {
            db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  ✗ DB connection failed: {Markup.Escape(ex.Message)}[/]");
            FooterBar.PressAnyKey();
            Console.ReadKey(intercept: true);
            return;
        }

        if (country.StartsWith("All"))
        {
            HeaderBar.Render("Integrity › CDT DB › All");
            AnsiConsole.WriteLine();

            var pages = new List<ReportPage>();

            foreach (var cc in new[] { "US", "CA", "MX" })
            {
                HeaderBar.Render($"Integrity › CDT DB › {cc}");

                var (exitCode, checkResults, reportPath) = await CdtDbIntegrity.RunForCountryAsync(
                    db, cc.ToUpperInvariant(), null);

                pages.Add(new ReportPage(cc, "CDT DB", File.Exists(reportPath) ? reportPath : null, exitCode, checkResults));

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

            var (exitCode, checkResults, reportPath) = await CdtDbIntegrity.RunForCountryAsync(
                db, country.ToUpperInvariant(), null);

            AnsiConsole.WriteLine();
            ShowResultBanner(exitCode);
            ShowReportPath(reportPath);
            FooterBar.PressAnyKey();
            Console.ReadKey(intercept: true);
        }
    }

    // ── Shared browser (← → navigation over per-country report pages) ─────────

    private static void BrowseReports(List<ReportPage> pages)
    {
        var index = 0;

        while (true)
        {
            var page = pages[index];

            var prevLabel   = index > 0 ? $"[bold]← {pages[index - 1].Country}[/]" : "[grey]←[/]";
            var nextLabel   = index < pages.Count - 1 ? $"[bold]{pages[index + 1].Country} →[/]" : "[grey]→[/]";
            var statusLabel = page.ExitCode == 0 ? "[green]✓ passed[/]" : "[red]✗ issues[/]";

            HeaderBar.Render($"Integrity › {page.Mode} › {page.Country}  {statusLabel}");

            CdtCommandMenu.Render(
                $"  {prevLabel}    ({index + 1}/{pages.Count})    {nextLabel}    [grey]Esc: back[/]");
            AnsiConsole.WriteLine();

            if (page.CheckResults is not null)
            {
                CdtDbIntegrity.PrintSummaryTable(page.Country, page.CheckResults);
                if (page.ReportPath is not null)
                    AnsiConsole.MarkupLine($"  [grey]Full report: {Markup.Escape(page.ReportPath)}[/]");
            }
            else if (page.ZplSummary is not null)
            {
                IntegrityDisplay.PrintSummary(page.Country, page.ZplSummary);
                if (page.ReportPath is not null)
                    AnsiConsole.MarkupLine($"  [grey]Full report: {Markup.Escape(page.ReportPath)}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]  No results available.[/]");
            }

            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.LeftArrow  when index > 0:               index--; break;
                case ConsoleKey.RightArrow when index < pages.Count - 1: index++; break;
                case ConsoleKey.Escape:                                   return;
            }
        }
    }

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
