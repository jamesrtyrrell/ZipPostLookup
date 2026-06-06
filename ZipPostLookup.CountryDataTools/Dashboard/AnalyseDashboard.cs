using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class AnalyseDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]Analyse[/]").LeftJustified());
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  Analyses curated reference data and writes a Markdown report.");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("  Country:")
                    .UseConverter(s => s switch
                    {
                        "All (US + CA + MX)" => $"[bold cyan]{"All",-10}[/]  [grey]Run all three, reports in DataAnalysis/[/]",
                        "← Back"             => "[grey]← Back[/]",
                        _                    => $"[bold cyan]{s}[/]",
                    })
                    .AddChoices("US", "CA", "MX", "All (US + CA + MX)", "← Back"));

            if (choice == "← Back") break;

            var output = AnsiConsole.Prompt(
                new TextPrompt<string>("  Output path [grey](blank = default DataAnalysis/ dir)[/]:")
                    .AllowEmpty());

            var args = new List<string>();
            if (choice.StartsWith("All")) args.Add("--all");
            else args.AddRange(["--country", choice]);
            if (!string.IsNullOrWhiteSpace(output)) args.AddRange(["--output", output]);

            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]analyse[/]").LeftJustified());
            AnsiConsole.WriteLine();

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
