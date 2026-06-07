using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class EnrichDashboard
{
    private sealed record Subcommand(string Key, string Name, string Description);

    private static readonly Subcommand Candidates = new("candidates", "candidates", "Resolve discrepancies in a pipeline run via the API pool");
    private static readonly Subcommand Direct     = new("direct",     "direct",     "Backfill uncurated reference rows directly");
    private static readonly Subcommand Ref        = new("ref",        "ref",        "Enrich from coordinates file or API provider");
    private static readonly Subcommand Back       = new("back",       "← Back",     "");

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            DashboardRenderer.RenderHeader("Enrich");

            var selected = MenuPrompt.Show(
                [Candidates, Direct, Ref, Back],
                s => s == Back
                    ? "[grey]← Back[/]"
                    : $"[bold cyan]{s.Name,-14}[/]  [grey]{s.Description}[/]",
                escapeReturns: Back,
                title: "Select enrichment mode:");

            if (selected == Back)
                break;

            // enrich ref has too many option shapes (file path vs provider) — show help for now.
            if (selected == Ref)
            {
                DashboardRenderer.RenderHeader("Enrich › ref");
                await EnrichCommand.RunAsync(["-h"]);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
                Console.ReadKey(intercept: true);
                continue;
            }

            DashboardRenderer.RenderHeader($"Enrich › {selected.Name}");

            var countryChoice = MenuPrompt.Show(
                ["US", "CA", "MX", "All (US + CA + MX)", "← Cancel"],
                s => s switch
                {
                    "← Cancel"           => "[grey]← Cancel[/]",
                    "All (US + CA + MX)" => $"[bold cyan]{"All",-10}[/]  [grey]US + CA + MX[/]",
                    _                    => $"[bold cyan]{s}[/]",
                },
                escapeReturns: "← Cancel",
                title: "Country:");

            if (countryChoice == "← Cancel") continue;

            var limit = AnsiConsole.Prompt(
                new TextPrompt<int>("  Limit [grey](codes per run)[/]:")
                    .DefaultValue(100)
                    .Validate(n => n > 0
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Enter a positive number[/]")));

            var dryRun = AnsiConsole.Confirm("  Dry run?", false);

            string[] countryArgs = countryChoice.StartsWith("All")
                ? ["--all"]
                : ["--country", countryChoice];

            string[] dryRunArgs = dryRun ? ["--dry-run"] : [];

            string[] runArgs = [selected.Key, ..countryArgs, "--limit", limit.ToString(), ..dryRunArgs];

            DashboardRenderer.RenderHeader($"Enrich › {selected.Name}");

            var exitCode = await EnrichCommand.RunAsync(runArgs);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(exitCode == 0
                ? "[green]  ✓ Completed[/]"
                : $"[red]  ✗ Exited with code {exitCode}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]  Press any key to return to the menu...[/]");
            Console.ReadKey(intercept: true);
        }

        return 0;
    }
}
