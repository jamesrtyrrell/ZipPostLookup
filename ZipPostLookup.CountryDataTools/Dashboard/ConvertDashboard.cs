using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class ConvertDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]Convert[/]").LeftJustified());
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  Converts a GeoNames or OpenStreetMap TSV to a candidate CSV.");
            AnsiConsole.MarkupLine("  [grey]Supported: GeoNames postal TSV, OSM streets/addresses/houses TSV.[/]");
            AnsiConsole.WriteLine();

            var file = AnsiConsole.Prompt(
                new TextPrompt<string>("  Input TSV path [grey](or blank to go back)[/]:")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(file)) break;

            var countryOverride = AnsiConsole.Prompt(
                new TextPrompt<string>("  Country override [grey](blank = derive from filename or data)[/]:")
                    .AllowEmpty());

            var outputOverride = AnsiConsole.Prompt(
                new TextPrompt<string>("  Output path [grey](blank = {cc}-candidate.csv alongside input)[/]:")
                    .AllowEmpty());

            var noPrompts = AnsiConsole.Confirm("  --no-prompts (skip format confirmation)?", false);

            var args = new List<string> { file };
            if (!string.IsNullOrWhiteSpace(countryOverride)) args.AddRange(["--country", countryOverride]);
            if (!string.IsNullOrWhiteSpace(outputOverride))  args.AddRange(["--output",  outputOverride]);
            if (noPrompts) args.Add("--no-prompts");

            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]convert[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var exitCode = await ConvertKnownFormatsCommand.RunAsync([.. args]);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(exitCode == 0
                ? "[green]  ✓ Conversion complete[/]"
                : $"[red]  ✗ Exited with code {exitCode}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
            Console.ReadKey(intercept: true);
        }

        return 0;
    }
}
