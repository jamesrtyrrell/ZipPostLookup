using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class ValidateDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Validate");
            AnsiConsole.MarkupLine("  Validates a candidate CSV and guides you through fix/extract steps.");
            AnsiConsole.WriteLine();

            var file = AnsiConsole.Prompt(
                new TextPrompt<string>("  Candidate CSV path [grey](or blank to go back)[/]:")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(file)) break;

            var country = CountryPicker.Show(
                title: "Country:",
                cancelLabel: "← Cancel");

            if (country == "← Cancel") continue;

            var noPrompts = AnsiConsole.Confirm("  --no-prompts (apply fix + extract automatically)?", false);

            HeaderBar.Render("Validate");

            var exitCode = await ValidateCommand.RunAsync(
                new ValidateCommand.Options(
                    File:      file,
                    Country:   country,
                    Report:    null,
                    NoPrompts: noPrompts));

            FooterBar.ShowResultAndPause(exitCode);
        }

        return 0;
    }
}
