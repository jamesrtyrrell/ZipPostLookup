using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;
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

            var choice = CdtSelectMenu.Show(
                ["US", "CA", "MX", "All (US + CA + MX)", "← Back"],
                s => s switch
                {
                    "All (US + CA + MX)" => $"[bold cyan]{"All",-10}[/]  [grey]Run all three, reports in DataAnalysis/[/]",
                    "← Back"             => "[grey]← Back[/]",
                    _                    => $"[bold cyan]{s}[/]",
                },
                escapeReturns: "← Back",
                title: "Country:");

            if (choice == "← Back") break;

            HeaderBar.Render("Analyse");

            var output = AnsiConsole.Prompt(
                new TextPrompt<string>("  Output path [grey](blank = default DataAnalysis/ dir)[/]:")
                    .AllowEmpty());

            var args = new List<string>();
            if (choice.StartsWith("All")) args.Add("--all");
            else args.AddRange(["--country", choice]);
            if (!string.IsNullOrWhiteSpace(output)) args.AddRange(["--output", output]);

            HeaderBar.Render("Analyse");

            var exitCode = await AnalyseCommand.RunAsync([.. args]);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(exitCode == 0
                ? "[green]  ✓ Report written[/]"
                : $"[red]  ✗ Exited with code {exitCode}[/]");
            AnsiConsole.WriteLine();
            FooterBar.PressAnyKey();
            Console.ReadKey(intercept: true);
        }

        return 0;
    }
}
