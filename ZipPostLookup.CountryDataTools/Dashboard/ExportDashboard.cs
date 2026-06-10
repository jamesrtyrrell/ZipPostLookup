using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class ExportDashboard
{
    private sealed record Target(string Key, string Label, string Desc, ExportReferenceCommand.ExportTarget ExportTarget);

    private static readonly Target TargetRef  = new("ref",  "ref",  "Source-of-truth reference CSV → CountryDataTools/Data/{cc}/", ExportReferenceCommand.ExportTarget.Ref);
    private static readonly Target TargetMain = new("main", "main", "Optimised library CSV → ZipPostLookup/Data/{cc}/",            ExportReferenceCommand.ExportTarget.Main);
    private static readonly Target TargetZpi  = new("zpi",  "zpi",  "Frozen binary ZPI image → ZipPostLookup/Data/{cc}/",          ExportReferenceCommand.ExportTarget.Zpi);
    private static readonly Target TargetBack = new("back", "← Back", "",                                                          ExportReferenceCommand.ExportTarget.Main);

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Export");

            var target = CdtSelectMenu.Show(
                [TargetRef, TargetMain, TargetZpi, TargetBack],
                t => t == TargetBack
                    ? "[grey]← Back[/]"
                    : $"[bold cyan]{t.Label,-8}[/]  [grey]{t.Desc}[/]",
                escapeReturns: TargetBack,
                title: "Export target:");

            if (target == TargetBack) break;

            HeaderBar.Render($"Export › {target.Key}");

            var countryChoice = CountryPicker.Show(
                title: "Country:",
                cancelLabel: "← Cancel",
                allLabel: "All (US + CA + MX)",
                allDescription: "Run all three in sequence");

            if (countryChoice == "← Cancel") continue;

            var curatedOnly  = AnsiConsole.Confirm("  --curated-only (skip non-curated rows)?", true);
            var uncompressed = target == TargetZpi
                && AnsiConsole.Confirm("  --uncompressed (write raw .zpi instead of .zpi.br)?", false);

            var isAll = countryChoice.StartsWith("All");
            var country = isAll ? "" : countryChoice;

            HeaderBar.Render($"Export › {target.Key}");

            var exitCode = await ExportReferenceCommand.RunAsync(
                new ExportReferenceCommand.Options(
                    Country:      country,
                    Target:       target.ExportTarget,
                    Output:       "",
                    CuratedOnly:  curatedOnly,
                    Uncompressed: uncompressed,
                    All:          isAll,
                    FromCsv:      null));

            FooterBar.ShowResultAndPause(exitCode, "Export complete");
        }

        return 0;
    }
}
