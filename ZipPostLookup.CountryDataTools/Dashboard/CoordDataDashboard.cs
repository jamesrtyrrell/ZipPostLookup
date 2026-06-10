using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class CoordDataDashboard
{
    public static async Task<int> RunAsync()
    {
        HeaderBar.Render("Coord Data");

        var source = AnsiConsole.Prompt(
            new TextPrompt<string>("  Source CSV path:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        var countryChoice = CountryPicker.Show(
            title: "Country (optional):",
            cancelLabel: "← Cancel",
            anyLabel: "Any (no filter)");

        if (countryChoice == "← Cancel") return 0;

        var batch = AnsiConsole.Prompt(
            new TextPrompt<int>("  Batch size [grey](default 1000)[/]:")
                .DefaultValue(1000)
                .Validate(n => n > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be positive[/]")));

        var dryRun = AnsiConsole.Confirm("  Dry run?", false);

        var args = new List<string> { "coords", "--source", source };
        if (countryChoice != "Any (no filter)") args.AddRange(["--country", countryChoice]);
        args.AddRange(["--batch", batch.ToString()]);
        if (dryRun) args.Add("--dry-run");

        HeaderBar.Render("Coord Data › coords");
        var exitCode = await IngestCommand.RunAsync([.. args]);

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }
}
