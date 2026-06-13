using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class CodesOnlyDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("ZpCodes Only");

            var file = AnsiConsole.Prompt(
                new TextPrompt<string>("  Codes file path [grey](csv of bare codes, one per line or comma-separated)[/]:")
                    .Validate(s => !string.IsNullOrWhiteSpace(s)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Path is required[/]")));

            if (!File.Exists(file))
            {
                AnsiConsole.MarkupLine("[red]  File not found.[/]");
                AnsiConsole.WriteLine();
                if (!AnsiConsole.Confirm("  Try again?", true)) break;
                continue;
            }

            var country = CountryPicker.Show(
                title: "Country:",
                cancelLabel: "← Cancel");

            if (country == "← Cancel") break;

            var dryRun = AnsiConsole.Confirm("  --dry-run (validate and count without inserting)?", false);

            HeaderBar.Render("ZpCodes Only");

            var exitCode = await ImportCodesOnlyCommand.RunAsync(
                new ImportCodesOnlyCommand.Options(File: file, Country: country, DryRun: dryRun));

            FooterBar.ShowResultAndPause(exitCode);
            break;
        }

        return 0;
    }
}
