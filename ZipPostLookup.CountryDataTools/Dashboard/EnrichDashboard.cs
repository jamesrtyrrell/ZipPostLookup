using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

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
            HeaderBar.Render("Enrich");

            var selected = CdtSelectMenu.Show(
                [Candidates, Direct, Ref, Back],
                s => s == Back
                    ? "[grey]← Back[/]"
                    : $"[bold cyan]{s.Name,-14}[/]  [grey]{s.Description}[/]",
                escapeReturns: Back,
                title: "Select enrichment mode:");

            if (selected == Back)
                break;

            if (selected == Ref)
            {
                await RunRefAsync();
                continue;
            }

            HeaderBar.Render($"Enrich › {selected.Name}");

            var countryChoice = CountryPicker.Show(
                title: "Country:",
                cancelLabel: "← Cancel",
                allLabel: "All (US + CA + MX)",
                allDescription: "US + CA + MX");

            if (countryChoice == "← Cancel") continue;

            var limit = AnsiConsole.Prompt(
                new TextPrompt<int>("  Limit [grey](codes per run)[/]:")
                    .DefaultValue(100)
                    .Validate(n => n > 0
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Enter a positive number[/]")));

            var dryRun = AnsiConsole.Confirm("  Dry run?", false);

            var isAll = countryChoice.StartsWith("All");
            var country = isAll ? "" : countryChoice;

            int exitCode;
            if (selected == Candidates)
            {
                exitCode = await EnrichCandidatesCommand.RunAsync(
                    new EnrichCandidatesCommand.Options(
                        Country: country,
                        RunId:   "",
                        Limit:   limit,
                        DryRun:  dryRun,
                        All:     isAll));
            }
            else
            {
                exitCode = await EnrichDirectCommand.RunAsync(
                    new EnrichDirectCommand.Options(
                        Country: country,
                        Limit:   limit,
                        DryRun:  dryRun,
                        All:     isAll));
            }

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

    private static async Task RunRefAsync()
    {
        HeaderBar.Render("Enrich › ref");

        var source = AnsiConsole.Prompt(
            new TextPrompt<string>("  Source CSV path:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        var countryChoice = CountryPicker.Show(
            title: "Country (optional):",
            cancelLabel: "← Cancel",
            anyLabel: "Any (no filter)");

        if (countryChoice == "← Cancel") return;

        var batch = AnsiConsole.Prompt(
            new TextPrompt<int>("  Batch size [grey](default 1000)[/]:")
                .DefaultValue(1000)
                .Validate(n => n > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be positive[/]")));

        var dryRun = AnsiConsole.Confirm("  Dry run?", false);

        var country = countryChoice == "Any (no filter)" ? "US" : countryChoice;

        HeaderBar.Render("Enrich › ref › coords");

        var exitCode = await EnrichReferenceFromCoordinatesCommand.RunAsync(
            new EnrichReferenceFromCoordinatesCommand.Options(
                Source:    source,
                Country:   country,
                BatchSize: batch,
                DryRun:    dryRun));

        FooterBar.ShowResultAndPause(exitCode);
    }
}
