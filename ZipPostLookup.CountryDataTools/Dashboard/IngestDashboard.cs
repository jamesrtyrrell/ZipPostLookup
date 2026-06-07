using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class IngestDashboard
{
    private sealed record Sub(string Key, string Label, string Desc);

    private static readonly Sub SubRef       = new("ref",       "ref",       "Seed data.reference from the embedded reference CSV");
    private static readonly Sub SubCandidate = new("candidate", "candidate", "Import a candidate CSV against reference data");
    private static readonly Sub SubCoords    = new("coords",    "coords",    "Bulk-resolve timezones from a coordinates CSV");
    private static readonly Sub SubBack      = new("back",      "← Back",   "");

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            DashboardRenderer.RenderHeader("Ingest");

            var selected = MenuPrompt.Show(
                [SubRef, SubCandidate, SubCoords, SubBack],
                s => s == SubBack
                    ? "[grey]← Back[/]"
                    : $"[bold cyan]{s.Label,-14}[/]  [grey]{s.Desc}[/]",
                escapeReturns: SubBack,
                title: "Select ingest mode:");

            if (selected == SubBack) break;

            var exitCode = selected.Key switch
            {
                "ref"       => await RunRefAsync(),
                "candidate" => await RunCandidateAsync(),
                "coords"    => await RunCoordsAsync(),
                _           => 0,
            };

            _ = exitCode;
        }
        return 0;
    }

    // ── ref ───────────────────────────────────────────────────────────────────

    private static async Task<int> RunRefAsync()
    {
        DashboardRenderer.RenderHeader("Ingest › ref");

        var choice = MenuPrompt.Show(
            ["US", "CA", "MX", "All (US + CA + MX)", "← Cancel"],
            s => s switch
            {
                "← Cancel"           => "[grey]← Cancel[/]",
                "All (US + CA + MX)" => $"[bold cyan]{"All",-10}[/]  [grey]Import all three in sequence[/]",
                _                    => $"[bold cyan]{s}[/]",
            },
            escapeReturns: "← Cancel",
            title: "Country:");

        if (choice == "← Cancel") return 0;

        var force    = AnsiConsole.Confirm("  --force (re-import even if rows already exist)?", false);
        var infoOnly = AnsiConsole.Confirm("  --info-only (seed country_info only, skip reference rows)?", false);

        var args = new List<string> { "ref" };
        if (choice.StartsWith("All")) args.Add("--all");
        else args.AddRange(["--country", choice]);
        if (force)    args.Add("--force");
        if (infoOnly) args.Add("--info-only");

        return await RunAndPause("ingest ref", args);
    }

    // ── candidate ─────────────────────────────────────────────────────────────

    private static async Task<int> RunCandidateAsync()
    {
        DashboardRenderer.RenderHeader("Ingest › candidate");

        var file = AnsiConsole.Prompt(
            new TextPrompt<string>("  Candidate CSV path:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        var country = MenuPrompt.Show(
            ["US", "CA", "MX", "← Cancel"],
            s => s == "← Cancel" ? "[grey]← Cancel[/]" : $"[bold cyan]{s}[/]",
            escapeReturns: "← Cancel",
            title: "Country:");

        if (country == "← Cancel") return 0;

        return await RunAndPause("ingest candidate", ["candidate", file, "--country", country]);
    }

    // ── coords ────────────────────────────────────────────────────────────────

    private static async Task<int> RunCoordsAsync()
    {
        DashboardRenderer.RenderHeader("Ingest › coords");

        var source = AnsiConsole.Prompt(
            new TextPrompt<string>("  Source CSV path:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        var countryChoice = MenuPrompt.Show(
            ["US", "CA", "MX", "Any (no filter)", "← Cancel"],
            s => s switch
            {
                "← Cancel"        => "[grey]← Cancel[/]",
                "Any (no filter)" => "[grey]Any (no filter)[/]",
                _                 => $"[bold cyan]{s}[/]",
            },
            escapeReturns: "← Cancel",
            title: "Country (optional):");

        if (countryChoice == "← Cancel") return 0;

        var batch = AnsiConsole.Prompt(
            new TextPrompt<int>("  Batch size [grey](default 1000)[/]:")
                .DefaultValue(1000)
                .Validate(n => n > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be positive[/]")));

        var dryRun = AnsiConsole.Confirm("  Dry run?", false);

        var args = new List<string> { "coords", "--source", source };
        if (countryChoice != "Any (no filter)") args.AddRange(["--country", countryChoice]);
        args.AddRange(["--batch", batch.ToString()]);
        if (dryRun) args.Add("--dry-run");

        return await RunAndPause("ingest coords", args);
    }

    // ── shared run + pause ────────────────────────────────────────────────────

    private static async Task<int> RunAndPause(string label, List<string> args)
    {
        DashboardRenderer.RenderHeader(label);

        var exitCode = await IngestCommand.RunAsync([.. args]);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(exitCode == 0
            ? "[green]  ✓ Done[/]"
            : $"[red]  ✗ Exited with code {exitCode}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
        Console.ReadKey(intercept: true);
        return exitCode;
    }
}
