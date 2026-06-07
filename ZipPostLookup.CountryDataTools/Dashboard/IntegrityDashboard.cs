using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class IntegrityDashboard
{
    private sealed record ReportPage(string Country, string? ReportPath, int ExitCode);

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            DashboardRenderer.RenderHeader("Integrity");

            var choice = MenuPrompt.Show(
                ["US", "CA", "MX", "All (US + CA + MX)", "← Back"],
                s => s switch
                {
                    "← Back"             => "[grey]← Back[/]",
                    "All (US + CA + MX)" => $"[bold cyan]{"All",-14}[/]  [grey]Run all three, then browse results with ← →[/]",
                    _                    => $"[bold cyan]{s,-14}[/]",
                },
                escapeReturns: "← Back",
                title: "Select country to check:");

            if (choice == "← Back")
                break;

            DashboardRenderer.RenderHeader($"Integrity › {(choice.StartsWith("All") ? "All" : choice)}");

            var tests = AnsiConsole.Prompt(
                new TextPrompt<int>("  Tests [grey](default 1000)[/]:")
                    .DefaultValue(1000)
                    .Validate(n => n > 0
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Enter a positive number[/]")));

            AnsiConsole.WriteLine();

            if (choice.StartsWith("All"))
            {
                await RunAllAndBrowseAsync(["US", "CA", "MX"], tests);
            }
            else
            {
                DashboardRenderer.RenderHeader($"Integrity › {choice}");
                await IntegrityCheckCommand.RunAsync(["--country", choice, "--tests", tests.ToString()]);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
                Console.ReadKey(intercept: true);
            }
        }

        return 0;
    }

    private static async Task RunAllAndBrowseAsync(string[] countries, int tests)
    {
        var pages    = new List<ReportPage>();
        var baseDir  = Path.Combine(Directory.GetCurrentDirectory(), "DataAnalysis");
        var datestamp = DateTime.UtcNow.ToString("yyyyMMdd");

        foreach (var cc in countries)
        {
            DashboardRenderer.RenderHeader($"Integrity › {cc}");

            // Mirror the path the handler will write to (handler uses DateTime.UtcNow at time of
            // write, so computing it right before the run keeps dates in sync for normal runs).
            var reportPath = Path.Combine(baseDir,
                $"{cc.ToLowerInvariant()}-integrity-{datestamp}.md");

            var exitCode = await IntegrityCheckCommand.RunAsync(
                ["--country", cc, "--tests", tests.ToString()]);

            pages.Add(new ReportPage(cc, File.Exists(reportPath) ? reportPath : null, exitCode));

            if (cc != countries[^1])
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Next country in 2 s...[/]");
                await Task.Delay(2_000);
            }
        }

        BrowseReports(pages);
    }

    private static void BrowseReports(List<ReportPage> pages)
    {
        var index = 0;

        while (true)
        {
            var page = pages[index];

            var prevLabel = index > 0
                ? $"[bold]← {pages[index - 1].Country}[/]"
                : "[grey]←[/]";
            var nextLabel = index < pages.Count - 1
                ? $"[bold]{pages[index + 1].Country} →[/]"
                : "[grey]→[/]";
            var statusLabel = page.ExitCode == 0
                ? "[green]✓ passed[/]"
                : "[red]✗ failures[/]";

            DashboardRenderer.RenderHeader($"Integrity › {page.Country}  {statusLabel}");
            AnsiConsole.MarkupLine($"  {prevLabel}    ({index + 1}/{pages.Count})    {nextLabel}    [grey]Esc: back[/]");
            AnsiConsole.WriteLine();

            // ── Report content ────────────────────────────────────────────────
            if (page.ReportPath is not null)
                Console.Write(File.ReadAllText(page.ReportPath));
            else
                AnsiConsole.MarkupLine("[grey]  No report file found for this country.[/]");

            // ── Key input ─────────────────────────────────────────────────────
            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.LeftArrow  when index > 0:                  index--; break;
                case ConsoleKey.RightArrow when index < pages.Count - 1:    index++; break;
                case ConsoleKey.Escape:                                      return;
            }
        }
    }
}
