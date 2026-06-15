using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class AutoPromoteDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Auto-Promote Aliases");
            AnsiConsole.MarkupLine("  Phase 3: Auto-promote candidate aliases using PlaceNameNormalizer.");
            AnsiConsole.MarkupLine("  [grey]Compares unresolved Name discrepancies against existing place names.[/]");
            AnsiConsole.WriteLine();

            var choice = CountryPicker.Show(
                title: "Country:",
                cancelLabel: "← Back",
                allLabel: "All (US + CA + MX)",
                allDescription: "Run all three countries");

            if (choice == "← Back") break;

            HeaderBar.Render("Auto-Promote Aliases");

            var dryRun = AnsiConsole.Confirm("  Dry-run [grey](show matches without writing)[/]?", defaultValue: true);

            var isAll   = choice.StartsWith("All");
            var country = isAll ? "" : choice;

            HeaderBar.Render("Auto-Promote Aliases");

            var exitCode = await AutoPromoteAliasesCommand.RunAsync(
                new AutoPromoteAliasesCommand.Options(
                    Country: country,
                    DryRun:  dryRun,
                    All:     isAll));

            FooterBar.ShowResultAndPause(exitCode, "Aliases promoted");
        }

        return 0;
    }
}
