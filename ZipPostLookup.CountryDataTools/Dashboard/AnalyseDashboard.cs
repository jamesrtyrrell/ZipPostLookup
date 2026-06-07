using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class AnalyseDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            DashboardRenderer.RenderHeader("Analyse");
            AnsiConsole.MarkupLine("  Analyses curated reference data and writes a Markdown report.");
            AnsiConsole.WriteLine();

            var choice = MenuPrompt.Show(
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

            DashboardRenderer.RenderHeader("Analyse");

            var output = AnsiConsole.Prompt(
                new TextPrompt<string>("  Output path [grey](blank = default DataAnalysis/ dir)[/]:")
                    .AllowEmpty());

            var args = new List<string>();
            if (choice.StartsWith("All")) args.Add("--all");
            else args.AddRange(["--country", choice]);
            if (!string.IsNullOrWhiteSpace(output)) args.AddRange(["--output", output]);

            DashboardRenderer.RenderHeader("Analyse");

            var exitCode = await AnalyseCommand.RunAsync([.. args]);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(exitCode == 0
                ? "[green]  ✓ Report written[/]"
                : $"[red]  ✗ Exited with code {exitCode}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
            Console.ReadKey(intercept: true);
        }

        return 0;
    }
}
