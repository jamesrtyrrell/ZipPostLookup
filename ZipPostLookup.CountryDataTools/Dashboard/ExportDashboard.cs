using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class ExportDashboard
{
    private sealed record Target(string Key, string Label, string Desc);

    private static readonly Target TargetRef  = new("ref",  "ref",  "Source-of-truth reference CSV → CountryDataTools/Data/{cc}/");
    private static readonly Target TargetMain = new("main", "main", "Optimised library CSV → ZipPostLookup/Data/{cc}/");
    private static readonly Target TargetZpi  = new("zpi",  "zpi",  "Frozen binary ZPI image → ZipPostLookup/Data/{cc}/");
    private static readonly Target TargetBack = new("back", "← Back", "");

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

            var countryChoice = CdtSelectMenu.Show(
                ["US", "CA", "MX", "All (US + CA + MX)", "← Cancel"],
                s => s switch
                {
                    "← Cancel"           => "[grey]← Cancel[/]",
                    "All (US + CA + MX)" => $"[bold cyan]{"All",-10}[/]  [grey]Run all three in sequence[/]",
                    _                    => $"[bold cyan]{s}[/]",
                },
                escapeReturns: "← Cancel",
                title: "Country:");

            if (countryChoice == "← Cancel") continue;

            var curatedOnly  = AnsiConsole.Confirm("  --curated-only (skip non-curated rows)?", true);
            var uncompressed = target == TargetZpi
                && AnsiConsole.Confirm("  --uncompressed (write raw .zpi instead of .zpi.br)?", false);

            var args = new List<string> { "--target", target.Key };
            if (countryChoice.StartsWith("All")) args.Add("--all");
            else args.AddRange(["--country", countryChoice]);
            if (curatedOnly)  args.Add("--curated-only");
            if (uncompressed) args.Add("--uncompressed");

            HeaderBar.Render($"Export › {target.Key}");

            var exitCode = await ExportCommand.RunAsync([.. args]);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(exitCode == 0
                ? "[green]  ✓ Export complete[/]"
                : $"[red]  ✗ Exited with code {exitCode}[/]");
            AnsiConsole.WriteLine();
            FooterBar.PressAnyKey();
            Console.ReadKey(intercept: true);
        }

        return 0;
    }
}
