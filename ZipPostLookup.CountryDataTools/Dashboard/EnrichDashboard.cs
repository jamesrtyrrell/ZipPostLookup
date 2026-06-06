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
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]Enrich[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<Subcommand>()
                    .Title("Select enrichment mode:")
                    .UseConverter(s => s == Back
                        ? "[grey]← Back[/]"
                        : $"[bold cyan]{s.Name,-14}[/]  [grey]{s.Description}[/]")
                    .AddChoices(Candidates, Direct, Ref, Back));

            if (selected == Back)
                break;

            // enrich ref has too many option shapes (file path vs provider) — show help for now.
            if (selected == Ref)
            {
                AnsiConsole.Clear();
                AnsiConsole.Write(new Rule("[bold cyan]enrich ref[/]").LeftJustified());
                AnsiConsole.WriteLine();
                await EnrichCommand.RunAsync(["-h"]);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
                Console.ReadKey(intercept: true);
                continue;
            }

            // ── Configure and run candidates / direct ────────────────────────────

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [bold]enrich {selected.Name}[/] — configure run");
            AnsiConsole.WriteLine();

            var countryChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("  Country:")
                    .AddChoices("US", "CA", "MX", "All (US + CA + MX)"));

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

            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule($"[bold cyan]enrich {selected.Name}[/]").LeftJustified());
            AnsiConsole.WriteLine();

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
