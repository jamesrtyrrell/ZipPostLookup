using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Utilities;

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

            file = FileTools.StripPathQuotes(file);
            if (string.IsNullOrWhiteSpace(file)) break;

            var countryOverride = AnsiConsole.Prompt(
                new TextPrompt<string>("  Country override [grey](blank = derive from filename or data)[/]:")
                    .AllowEmpty());

            var outputOverride = FileTools.StripPathQuotes(AnsiConsole.Prompt(
                new TextPrompt<string>("  Output path [grey](blank = {cc}-candidate.csv alongside input)[/]:")
                    .AllowEmpty()));

            var noPrompts = AnsiConsole.Confirm("  --no-prompts (skip format confirmation)?", false);

            HeaderBar.Render("Convert");

            var exitCode = await ConvertKnownFormatsCommand.RunAsync(
                new ConvertKnownFormatsCommand.Options(
                    File:      file,
                    Country:   string.IsNullOrWhiteSpace(countryOverride) ? null : countryOverride,
                    Output:    string.IsNullOrWhiteSpace(outputOverride)  ? null : outputOverride,
                    NoPrompts: noPrompts));

            FooterBar.ShowResultAndPause(exitCode, "Conversion complete");
        }

        return 0;
    }

    
}
