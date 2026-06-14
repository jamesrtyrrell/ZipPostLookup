using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.CountryRules;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Utilities;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class IngestDashboard
{
    private sealed record Sub(string Key, string Label, string Desc);

    private static readonly Sub SubRef       = new("ref",       "ref",       "Seed data.reference from the embedded reference CSV");
    private static readonly Sub SubCandidate = new("candidate", "candidate", "Import a candidate CSV against reference data");
    private static readonly Sub SubBack      = new("back",      "← Back",   "");

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Ingest");

            var selected = CdtSelectMenu.Show(
                [SubRef, SubCandidate, SubBack],
                s => s == SubBack
                    ? "[grey]← Back[/]"
                    : $"[bold cyan]{s.Label,-14}[/]  [grey]{s.Desc}[/]",
                escapeReturns: SubBack,
                title: "Select ingest mode:");

            if (selected == SubBack) break;

            var exitCode = selected.Key switch
            {
                "ref"       => await RunRefAsync(),
                "candidate" => await RunCandidateAsync(),
                _           => 0,
            };

            _ = exitCode;
        }
        return 0;
    }

    // ── ref ───────────────────────────────────────────────────────────────────

    private static async Task<int> RunRefAsync()
    {
        HeaderBar.Render("Ingest › ref");

        var choice = CountryPicker.Show(
            title: "Country:",
            cancelLabel: "← Cancel",
            allLabel: "All (US + CA + MX)",
            allDescription: "Import all three in sequence");

        if (choice == "← Cancel") return 0;

        var force    = AnsiConsole.Confirm("  --force (re-import even if rows already exist)?", false);
        var infoOnly = AnsiConsole.Confirm("  --info-only (seed country_info only, skip reference rows)?", false);

        var isAll   = choice.StartsWith("All");
        var country = isAll ? "" : choice;

        HeaderBar.Render("Ingest › ref");

        var exitCode = await ImportReferenceDataCommand.RunAsync(
            new ImportReferenceDataCommand.Options(
                Country:  country,
                Force:    force,
                InfoOnly: infoOnly,
                All:      isAll));

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }

    // ── candidate ─────────────────────────────────────────────────────────────

    private static async Task<int> RunCandidateAsync()
    {
        HeaderBar.Render("Ingest › candidate");

        var source = AnsiConsole.Prompt(
            new TextPrompt<string>("  Candidate CSV/TSV path:")
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

        // Sniff the delimiter and read a small preview for the column picker.
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

        var hasHeader  = AnsiConsole.Confirm("  Does the first row contain column headers?", true);
        var sampleRows = hasHeader ? preview.Skip(1).ToList() : preview;

        var country = CountryPicker.Show(title: "Country:", cancelLabel: "← Cancel");
        if (country == "← Cancel") { return 0; }

        // Ingestion star set; prefill the left column from the header row when present so the
        // picker (and the validation table beneath it) start populated for standard files.
        var mapping = ColumnMapping.ForIngestion();
        if (hasHeader) { mapping.BindByHeader(preview[0]); }

        // Values the file need not supply — shown on the left pane and in the validation table
        // so the user sees the full record: admin resolved at the country level from the code,
        // timezone created from coordinates, and the IsDefault default. Computed per row so it
        // tracks the bound ZpCode / Lat / Lng columns.
        var rules = CountryRulesFactory.For(country);

        IReadOnlyDictionary<string, string> Derive(ColumnMapping m, string[] row)
        {
            var d    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cmap = m.ToColumnMap();

            string? At(string field) =>
                cmap.TryGetValue(field, out var i) && i >= 0 && i < row.Length ? row[i].Trim() : null;

            var zip = At(nameof(CodesCandidate.ZpCode));
            if (!string.IsNullOrWhiteSpace(zip) && rules.ResolveAdmin1(zip) is { } a1)
            {
                d[nameof(CodesCandidate.Admin1)]     = a1.Name;
                d[nameof(CodesCandidate.Admin1Code)] = a1.Code;
            }

            var lat = At(nameof(CodesCandidate.Lat));
            var lng = At(nameof(CodesCandidate.Lng));
            if (!string.IsNullOrWhiteSpace(lat) && !string.IsNullOrWhiteSpace(lng)
                && TimezoneResolver.TryResolveWithCoordinates(lat, lng) is { } tz)
            {
                d[nameof(CodesCandidate.Timezone)] = rules.CanonicalizeTimezone(tz) ?? tz;
            }

            d[nameof(CodesCandidate.IsDefault)] = "false";
            return d;
        }

        var result = ColumnMappingWidget.Show(
            "Ingest › candidate › map columns", mapping, sampleRows,
            showValidation: true, derivedValues: Derive);
        if (result.Outcome == ColumnMappingOutcome.Cancel) { return 0; }

        // Accept — project every row through the mapping into a normalised standard candidate
        // CSV, then run the existing candidate-import pipeline (header validation + discrepancy
        // detection) on it. Reusing ImportCandidatesCommand keeps one ingestion path.
        var map  = result.Mapping.ToColumnMap();
        var rows = DelimitedFile.ReadRows(file, delimiter);
        var dataRows = hasHeader ? rows.Skip(1) : rows;

        string? Col(string[] r, string field) =>
            map.TryGetValue(field, out var i) && i >= 0 && i < r.Length ? r[i].Trim() : null;

        var csvRows = dataRows.Select(r => new CsvRow
        {
            ZpCode     = Col(r, nameof(CodesCandidate.ZpCode)),
            PlaceName  = Col(r, nameof(CodesCandidate.PlaceName)),
            Timezone   = Col(r, nameof(CodesCandidate.Timezone)),
            IsDefault  = Col(r, nameof(CodesCandidate.IsDefault)),
            Lat        = Col(r, nameof(CodesCandidate.Lat)),
            Lng        = Col(r, nameof(CodesCandidate.Lng)),
            Admin1     = Col(r, nameof(CodesCandidate.Admin1)),
            Admin1Code = Col(r, nameof(CodesCandidate.Admin1Code)),
        }).ToList();

        var tempPath = Path.Combine(Path.GetTempPath(), $"zpl-ingest-{Guid.NewGuid():N}.csv");
        int exitCode;
        try
        {
            CsvWriter.Write(csvRows, tempPath);

            HeaderBar.Render("Ingest › candidate");
            exitCode = await ImportCandidatesCommand.RunAsync(
                new ImportCandidatesCommand.Options(File: tempPath, Country: country));
        }
        finally
        {
            try { if (File.Exists(tempPath)) { File.Delete(tempPath); } }
            catch { /* best-effort temp cleanup */ }
        }

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }
}
