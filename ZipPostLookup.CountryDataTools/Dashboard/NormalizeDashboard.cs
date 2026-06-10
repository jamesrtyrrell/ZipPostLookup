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
                "normalize-tz"     => await RunNormalizeTzAsync(),
                "normalize-names"  => await RunNormalizeNamesAsync(),
                "normalize-admins" => await RunNormalizeAdminsAsync(),
                _                  => 0,
            };
        }

        return 0;
    }

    // ── normalize-tz ──────────────────────────────────────────────────────────

    private static async Task<int> RunNormalizeTzAsync()
    {
        HeaderBar.Render("Normalize › normalize-tz");

        var choice = CdtSelectMenu.Show(
            ["US", "CA", "MX", "All (US + CA + MX)", "← Cancel"],
            s => s switch
            {
                "← Cancel"           => "[grey]← Cancel[/]",
                "All (US + CA + MX)" => $"[bold cyan]{"All",-10}[/]  [grey]US + CA + MX in sequence[/]",
                _                    => $"[bold cyan]{s}[/]",
            },
            escapeReturns: "← Cancel",
            title: "Country:");

        if (choice == "← Cancel") return 0;

        string[] extra = choice.StartsWith("All")
            ? ["--all"]
            : ["--country", choice];

        return await RunAndPauseAsync("normalize-tz", extra);
    }

    // ── normalize-names ───────────────────────────────────────────────────────

    private static async Task<int> RunNormalizeNamesAsync()
    {
        HeaderBar.Render("Normalize › normalize-names");

        var choice = CdtSelectMenu.Show(
            ["US", "CA", "MX", "All (US + CA + MX)", "← Cancel"],
            s => s switch
            {
                "← Cancel"           => "[grey]← Cancel[/]",
                "All (US + CA + MX)" => $"[bold cyan]{"All",-10}[/]  [grey]US + CA + MX in sequence[/]",
                _                    => $"[bold cyan]{s}[/]",
            },
            escapeReturns: "← Cancel",
            title: "Country:");

        if (choice == "← Cancel") return 0;

        string[] extra = choice.StartsWith("All")
            ? ["--all"]
            : ["--country", choice];

        return await RunAndPauseAsync("normalize-names", extra);
    }

    // ── normalize-admins ──────────────────────────────────────────────────────

    private static async Task<int> RunNormalizeAdminsAsync()
    {
        HeaderBar.Render("Normalize › normalize-admins");

        var choice = CdtSelectMenu.Show(
            ["US", "MX", "All (US + MX)", "← Cancel"],
            s => s switch
            {
                "← Cancel"      => "[grey]← Cancel[/]",
                "All (US + MX)" => $"[bold cyan]{"All",-10}[/]  [grey]US + MX in sequence[/]",
                _               => $"[bold cyan]{s}[/]",
            },
            escapeReturns: "← Cancel",
            title: "Country:");

        if (choice == "← Cancel") return 0;

        string[] extra = choice.StartsWith("All")
            ? ["--all"]
            : ["--country", choice];

        return await RunAndPauseAsync("normalize-admins", extra);
    }

    // ── shared run + pause ────────────────────────────────────────────────────

    private static async Task<int> RunAndPauseAsync(string sub, string[] extra)
    {
        HeaderBar.Render($"Normalize › {sub}");

        var exitCode = await DbCommand.RunAsync([sub, .. extra]);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(exitCode == 0
            ? "[green]  ✓ Done[/]"
            : $"[red]  ✗ Exited with code {exitCode}[/]");
        AnsiConsole.WriteLine();
        FooterBar.PressAnyKey();
        Console.ReadKey(intercept: true);
        return exitCode;
    }
}
