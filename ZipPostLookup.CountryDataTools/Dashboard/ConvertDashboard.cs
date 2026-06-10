using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class ConvertDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Convert");
            AnsiConsole.MarkupLine("  Converts a GeoNames or OpenStreetMap TSV to a candidate CSV.");
            AnsiConsole.MarkupLine("  [grey]Supported: GeoNames postal TSV, OSM streets/addresses/houses TSV.[/]");
            AnsiConsole.WriteLine();

            var file = AnsiConsole.Prompt(
                new TextPrompt<string>("  Input TSV path [grey](or blank to go back)[/]:")
                    .AllowEmpty());

            file = StripPathQuotes(file);
            if (string.IsNullOrWhiteSpace(file)) break;

            var countryOverride = AnsiConsole.Prompt(
                new TextPrompt<string>("  Country override [grey](blank = derive from filename or data)[/]:")
                    .AllowEmpty());

            var outputOverride = StripPathQuotes(AnsiConsole.Prompt(
                new TextPrompt<string>("  Output path [grey](blank = {cc}-candidate.csv alongside input)[/]:")
                    .AllowEmpty()));

            var noPrompts = AnsiConsole.Confirm("  --no-prompts (skip format confirmation)?", false);

            var args = new List<string> { file };
            if (!string.IsNullOrWhiteSpace(countryOverride)) args.AddRange(["--country", countryOverride]);
            if (!string.IsNullOrWhiteSpace(outputOverride))  args.AddRange(["--output",  outputOverride]);
            if (noPrompts) args.Add("--no-prompts");

            HeaderBar.Render("Convert");

            var exitCode = await ConvertKnownFormatsCommand.RunAsync([.. args]);

            FooterBar.ShowResultAndPause(exitCode, "Conversion complete");
        }

        return 0;
    }

    private static string StripPathQuotes(string path)
    {
        path = path.Trim();
        if (path.Length >= 2 &&
            ((path[0] == '"'  && path[^1] == '"') ||
             (path[0] == '\'' && path[^1] == '\'')))
            path = path[1..^1].Trim();
        return path;
    }
}
