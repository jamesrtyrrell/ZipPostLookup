using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Utilities;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class CoordDataDashboard
{
    public static async Task<int> RunAsync()
    {
        HeaderBar.Render("Coord Data");

        var source = AnsiConsole.Prompt(
            new TextPrompt<string>("  Source CSV/TSV path:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));
        
        var file = FileTools.StripPathQuotes(source);
        
        if (!File.Exists(file))
        {
            AnsiConsole.MarkupLine("[red]  File not found.[/]");
            FooterBar.ShowResultAndPause(2);
            return 2;
        }

        // --- Sniff the delimiter and read a small preview for the column picker ---
        var delimiter = DelimitedFile.SniffDelimiter(file);
        var preview   = DelimitedFile.ReadRows(file, delimiter, maxRows: 7);

        if (preview.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]  No rows found in file.[/]");
            FooterBar.ShowResultAndPause(1);
            return 1;
        }

        AnsiConsole.MarkupLine($"  Delimiter: [cyan]{(delimiter == '\t' ? "TAB" : "comma")}[/]   " +
                               $"Columns: [cyan]{preview.Max(r => r.Length)}[/]");
        AnsiConsole.WriteLine();

        var hasHeader   = AnsiConsole.Confirm("  Does the first row contain column headers?", true);
        var sampleRows  = hasHeader ? preview.Skip(1).ToList() : preview;

        var country = CountryPicker.Show(title: "Country:", cancelLabel: "← Cancel");
        if (country == "← Cancel") return 0;

        // --- Column picker: ZpCode + Lat + Lng are the mandatory (*) fields for Coords ---
        var mapping = ColumnMapping.ForTemplate(nameof(Models.Dbo.CodesCandidate.Lat),
                                                nameof(Models.Dbo.CodesCandidate.Lng));

        var result = ColumnMappingWidget.Show("Coord Data › map columns", mapping, sampleRows);
        if (result.Outcome == ColumnMappingOutcome.Cancel) return 0;

        var map  = result.Mapping.ToColumnMap();
        int? city = map.TryGetValue(nameof(Models.Dbo.CodesCandidate.PlaceName), out var c) ? c : null;
        var columns = new EnrichReferenceFromCoordinatesCommand.ColumnIndexes(
            Zip:  map[nameof(Models.Dbo.CodesCandidate.ZpCode)],
            Lat:  map[nameof(Models.Dbo.CodesCandidate.Lat)],
            Lng:  map[nameof(Models.Dbo.CodesCandidate.Lng)],
            City: city);

        var batch = AnsiConsole.Prompt(
            new TextPrompt<int>("  Batch size [grey](default 1000)[/]:")
                .DefaultValue(1000)
                .Validate(n => n > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be positive[/]")));

        HeaderBar.Render("Coord Data › import");

        var exitCode = await EnrichReferenceFromCoordinatesCommand.RunAsync(
            new EnrichReferenceFromCoordinatesCommand.Options(
                Source:    source,
                Country:   country,
                BatchSize: batch),
            columns,
            delimiter,
            hasHeader);

        // Coordinate back-fill can leave timezones worth re-normalising (dedup aliases,
        // resolve any newly-complete pairs). Offer it right after the summary.
        if (exitCode == 0)
        {
            AnsiConsole.WriteLine();
            if (AnsiConsole.Confirm("  Do you want to normalize timezones?", false))
                await WorkDbCommand.RunNormalizeTzAsync();
        }

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }
}
