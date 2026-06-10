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
                "normalize-tz"     => await RunNormalizeTzAsync(),
                "normalize-names"  => await RunNormalizeWithPickerAsync("normalize-names",  ["US", "CA", "MX"], "All (US + CA + MX)", "US + CA + MX in sequence"),
                "normalize-admins" => await RunNormalizeWithPickerAsync("normalize-admins", ["US", "MX"],       "All (US + MX)",      "US + MX in sequence"),
                _                  => 0,
            };
        }

        return 0;
    }

    // ── normalize-tz: no country picker — always runs for all pipeline countries ─

    private static async Task<int> RunNormalizeTzAsync()
    {
        HeaderBar.Render("Normalize › normalize-tz");
        var exitCode = await WorkDbCommand.RunNormalizeTzAsync();
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
