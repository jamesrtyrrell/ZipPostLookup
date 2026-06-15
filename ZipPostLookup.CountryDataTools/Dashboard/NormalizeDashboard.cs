using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class NormalizeDashboard
{
    private sealed record Sub(string Key, string Label, string Desc, bool Future = false);

    private static readonly Sub[] Subs =
    [
        new("all",             "Normalize-All",    "Run all normalization steps in sequence", Future: true),
        new("normalize-tz",    "Normalize-Tz",     "Inherit alias coords/tz, reset blank-tz flags, canonicalise IANA aliases, resolve from coords"),
        new("normalize-names", "Normalize-Names",  "Detect and link place-name abbreviation alternates"),
        new("normalize-admins","Normalize-Admins", "Backfill missing admin1 from ZIP prefix rules"),
    ];

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Normalize");

            var back = new Sub("back", "← Back", "");

            var selected = CdtSelectMenu.Show(
                [.. Subs, back],
                s => s == back
                    ? "[grey]← Back[/]"
                    : s.Future
                        ? $"[grey]{s.Label,-18}  (coming soon)[/]"
                        : $"[bold cyan]{s.Label,-18}[/]  [grey]{s.Desc}[/]",
                escapeReturns: back,
                title: "Select operation:");

            if (selected == back) break;
            if (selected.Future) continue;

            _ = selected.Key switch
            {
                "normalize-tz"     => await RunNormalizeTzAsync(),
                "normalize-names"  => await RunNormalizeWithPickerAsync("normalize-names",  ["US", "CA", "MX"], "All (US + CA + MX)", "US + CA + MX in sequence"),
                "normalize-admins" => await RunNormalizeWithPickerAsync("normalize-admins", ["US", "MX"],       "All (US + MX)",      "US + MX in sequence"),
                _                  => 0,
            };
        }

        return 0;
    }

    // ── normalize-tz: no country picker — always runs for all pipeline countries ─

    private sealed record TzPolicy(string Key, string Label, string Desc);

    private static async Task<int> RunNormalizeTzAsync()
    {
        HeaderBar.Render("Normalize › normalize-tz");

        Spectre.Console.AnsiConsole.MarkupLine(
            "  Runs all of the following steps for [bold]US + CA + MX[/] in sequence:");
        Spectre.Console.AnsiConsole.MarkupLine(
            "  [grey]  1.[/] Propagate lat/lng + timezone from each principal row to its alias rows.");
        Spectre.Console.AnsiConsole.MarkupLine(
            "  [grey]  2.[/] Reset [white]TimezoneChecked=0[/] on rows that have a blank/null timezone.");
        Spectre.Console.AnsiConsole.MarkupLine(
            "  [grey]  3.[/] Canonicalise deprecated IANA timezone aliases (e.g. US/Eastern → America/New_York).");
        Spectre.Console.AnsiConsole.MarkupLine(
            "  [grey]  4.[/] Delete duplicate rows produced by the alias renaming.");
        Spectre.Console.AnsiConsole.MarkupLine(
            "  [grey]  5.[/] Normalise admin1 name variants to the dominant spelling.");
        Spectre.Console.AnsiConsole.MarkupLine(
            "  [grey]  6.[/] Resolve timezone from lat/lng for rows with [white]TimezoneChecked=0[/] and coordinates.");
        Spectre.Console.AnsiConsole.WriteLine();

        // Step 6 needs a policy when an existing timezone disagrees with the coordinate-derived one.
        var accept = new TzPolicy("accept", "Accept coordinates as truth",
            "Overwrite existing timezones that differ from lat/lng (+ mark verified)");
        var report = new TzPolicy("report", "Report differences only",
            "Leave existing timezones unchanged; list conflicts for review (default)");
        var cancel = new TzPolicy("cancel", "← Cancel", "");

        var choice = CdtSelectMenu.Show(
            [report, accept, cancel],
            p => p == cancel
                ? "[grey]← Cancel[/]"
                : $"[bold cyan]{p.Label,-28}[/]  [grey]{p.Desc}[/]",
            escapeReturns: cancel,
            title: "Step 6 — when a coordinate timezone differs from the existing value:");

        if (choice == cancel) return 0;

        HeaderBar.Render("Normalize › normalize-tz");
        var exitCode = await WorkDbCommand.RunNormalizeTzAsync(acceptCoordsOverride: choice == accept);
        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }

    // ── normalize-names / normalize-admins: country picker ───────────────────────

    private static async Task<int> RunNormalizeWithPickerAsync(
        string sub, IReadOnlyList<string> countries, string allLabel, string allDescription)
    {
        HeaderBar.Render($"Normalize › {sub}");

        var choice = CountryPicker.Show(
            title: "Country:",
            cancelLabel: "← Cancel",
            countries: countries,
            allLabel: allLabel,
            allDescription: allDescription);

        if (choice == "← Cancel") return 0;

        var isAll   = choice.StartsWith("All");
        var country = isAll ? "" : choice;
        var opts    = new WorkDbCommand.NormalizeOptions(country, isAll);

        HeaderBar.Render($"Normalize › {sub}");

        var exitCode = sub == "normalize-names"
            ? await WorkDbCommand.RunNormalizeNamesAsync(opts)
            : await WorkDbCommand.RunNormalizeAdminsAsync(opts);

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }
}
