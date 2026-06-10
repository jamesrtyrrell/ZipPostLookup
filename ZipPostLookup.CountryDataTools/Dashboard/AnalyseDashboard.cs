using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class AnalyseDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Analyse");
            AnsiConsole.MarkupLine("  Analyses curated reference data and writes a Markdown report.");
            AnsiConsole.WriteLine();

            var choice = CountryPicker.Show(
                title: "Country:",
                cancelLabel: "← Back",
                allLabel: "All (US + CA + MX)",
                allDescription: "Run all three, reports in DataAnalysis/");

            if (choice == "← Back") break;

            HeaderBar.Render("Analyse");

            var output = AnsiConsole.Prompt(
                new TextPrompt<string>("  Output path [grey](blank = default DataAnalysis/ dir)[/]:")
                    .AllowEmpty());

            var isAll   = choice.StartsWith("All");
            var country = isAll ? "" : choice;

            HeaderBar.Render("Analyse");

            var exitCode = await AnalyseCommand.RunAsync(
                new AnalyseCommand.Options(
                    Country: country,
                    Output:  output,
                    All:     isAll));

            FooterBar.ShowResultAndPause(exitCode, "Report written");
        }

        return 0;
    }
}
