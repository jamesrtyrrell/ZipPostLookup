using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class IngestDashboard
{
    private sealed record Sub(string Key, string Label, string Desc);

    private static readonly Sub SubRef       = new("ref",       "ref",       "Seed data.reference from the embedded reference CSV");
    private static readonly Sub SubCandidate = new("candidate", "candidate", "Import a candidate CSV against reference data");
    private static readonly Sub SubBack      = new("back",      "← Back",   "");

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Ingest");

            var selected = CdtSelectMenu.Show(
                [SubRef, SubCandidate, SubBack],
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
                _           => 0,
            };

            _ = exitCode;
        }
        return 0;
    }

    // ── ref ───────────────────────────────────────────────────────────────────

    private static async Task<int> RunRefAsync()
    {
        HeaderBar.Render("Ingest › ref");

        var choice = CountryPicker.Show(
            title: "Country:",
            cancelLabel: "← Cancel",
            allLabel: "All (US + CA + MX)",
            allDescription: "Import all three in sequence");

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
        HeaderBar.Render("Ingest › candidate");

        var file = AnsiConsole.Prompt(
            new TextPrompt<string>("  Candidate CSV path:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        var country = CountryPicker.Show(
            title: "Country:",
            cancelLabel: "← Cancel");

        if (country == "← Cancel") return 0;

        return await RunAndPause("ingest candidate", ["candidate", file, "--country", country]);
    }

    // ── shared run + pause ────────────────────────────────────────────────────

    private static async Task<int> RunAndPause(string label, List<string> args)
    {
        HeaderBar.Render(label);

        var exitCode = await IngestCommand.RunAsync([.. args]);

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }
}
