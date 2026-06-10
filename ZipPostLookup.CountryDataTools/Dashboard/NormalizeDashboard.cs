using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class NormalizeDashboard
{
    private sealed record Sub(string Key, string Label, string Desc, bool Future = false);

    private static readonly Sub[] Subs =
    [
        new("all",             "Normalize-All",    "Run all normalization steps in sequence", Future: true),
        new("normalize-tz",    "Normalize-Tz",     "Normalise timezone aliases and resolve from coordinates"),
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
                "normalize-tz"     => await RunNormalizeAsync("normalize-tz",    ["US", "CA", "MX"], "All (US + CA + MX)", "US + CA + MX in sequence"),
                "normalize-names"  => await RunNormalizeAsync("normalize-names", ["US", "CA", "MX"], "All (US + CA + MX)", "US + CA + MX in sequence"),
                "normalize-admins" => await RunNormalizeAsync("normalize-admins", ["US", "MX"],       "All (US + MX)",      "US + MX in sequence"),
                _                  => 0,
            };
        }

        return 0;
    }

    // ── shared: country picker → db <sub> ───────────────────────────────────────

    private static async Task<int> RunNormalizeAsync(
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

        string[] extra = choice.StartsWith("All")
            ? ["--all"]
            : ["--country", choice];

        return await RunAndPauseAsync(sub, extra);
    }

    // ── shared run + pause ────────────────────────────────────────────────────

    private static async Task<int> RunAndPauseAsync(string sub, string[] extra)
    {
        HeaderBar.Render($"Normalize › {sub}");

        var exitCode = await DbCommand.RunAsync([sub, .. extra]);

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }
}
