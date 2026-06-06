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
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]Ingest[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<Sub>()
                    .Title("Select ingest mode:")
                    .UseConverter(s => s == SubBack
                        ? "[grey]← Back[/]"
                        : $"[bold cyan]{s.Label,-14}[/]  [grey]{s.Desc}[/]")
                    .AddChoices(SubRef, SubCandidate, SubCoords, SubBack));

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
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold cyan]ingest ref[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  Country:")
                .UseConverter(s => s == "All (US + CA + MX)"
                    ? $"[bold cyan]{"All",-10}[/]  [grey]Import all three in sequence[/]"
                    : $"[bold cyan]{s}[/]")
                .AddChoices("US", "CA", "MX", "All (US + CA + MX)"));

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
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold cyan]ingest candidate[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var file = AnsiConsole.Prompt(
            new TextPrompt<string>("  Candidate CSV path:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        var country = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  Country:")
                .AddChoices("US", "CA", "MX"));

        return await RunAndPause("ingest candidate", ["candidate", file, "--country", country]);
    }

    // ── coords ────────────────────────────────────────────────────────────────

    private static async Task<int> RunCoordsAsync()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold cyan]ingest coords[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var source = AnsiConsole.Prompt(
            new TextPrompt<string>("  Source CSV path:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        var countryChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  Country [grey](optional)[/]:")
                .UseConverter(s => s == "Any (no filter)"
                    ? "[grey]Any (no filter)[/]"
                    : $"[bold cyan]{s}[/]")
                .AddChoices("US", "CA", "MX", "Any (no filter)"));

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
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold cyan]{label}[/]").LeftJustified());
        AnsiConsole.WriteLine();

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
