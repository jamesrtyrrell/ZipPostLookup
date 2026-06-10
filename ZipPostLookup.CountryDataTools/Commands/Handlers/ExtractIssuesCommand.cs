using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Validation.Csv;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools extractissues &lt;file.csv&gt; --country XX [--out extracted.csv]
///
/// Finds all rows that have at least one Severity.Error finding, writes them to
/// a sidecar CSV (default: {basename}-extracted-records.csv), and removes them
/// from the source file so the remaining data is structurally clean.
///
/// Fixable issues (duplicates, timezone conversion, etc.) are NOT extracted —
/// those are left for 'fix' to resolve automatically.
///
/// After extraction the source file is re-validated and a summary is printed.
/// </summary>
public static class ExtractIssuesCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        var file          = args.Length > 0 ? args[0] : "";
        var country       = args.OptionValue("--country") ?? "";
        var extractedFile = args.OptionValue("--out");

        if (string.IsNullOrEmpty(file)) { PrintUsage(); return 2; }

        if (!File.Exists(file))
        {
            await Console.Error.WriteLineAsync($"File not found: {file}");
            return 2;
        }

        if (!CommandArgs.ResolveCountry(file, country, out country)) return 2;

        Console.WriteLine($"Scanning {file} [{country.ToUpperInvariant()}] for structural errors…");

        var (rows, headerOk, missingCols) = CsvReader.Read(file);
        Console.WriteLine($"  Rows read: {rows.Count:N0}");

        var errors = ValidationRules.Validate(rows, headerOk, missingCols, country);

        int errorCount = errors.Count(e => e.Severity == Severity.Error);

        if (errorCount == 0)
        {
            Console.WriteLine("  ✓ No structural errors found — nothing to extract.");
            return 0;
        }

        Console.WriteLine($"  {errorCount} structural error(s) found across the following records.");

        // --- Build the set of record numbers that have at least one Error ---
        var errorRecords = errors
            .Where(e => e.Severity == Severity.Error)
            .Select(e => e.RecordNumber)
            .ToHashSet();

        var cleanRows     = rows.Where(r => !errorRecords.Contains(r.RecordNumber)).ToList();
        var extractedRows = rows.Where(r =>  errorRecords.Contains(r.RecordNumber)).ToList();

        Console.WriteLine($"  Rows to extract : {extractedRows.Count:N0}");
        Console.WriteLine($"  Rows remaining  : {cleanRows.Count:N0}");

        // --- Write extracted rows to sidecar file ---
        extractedFile ??= Path.Combine(
            Path.GetDirectoryName(file) ?? ".",
            Path.GetFileNameWithoutExtension(file) + "-extracted-records.csv");

        CsvWriter.Write(extractedRows, extractedFile);
        Console.WriteLine($"  Extracted to    : {extractedFile}");

        // --- Overwrite source with clean rows ---
        CsvWriter.Write(cleanRows, file);
        Console.WriteLine($"  Source updated  : {file}");

        // --- Re-validate the cleaned source ---
        Console.WriteLine();
        Console.WriteLine("Re-validating cleaned file…");
        var (cleanedRows, cleanedHeaderOk, cleanedMissingCols) = CsvReader.Read(file);
        var remainingErrors = ValidationRules.Validate(cleanedRows, cleanedHeaderOk, cleanedMissingCols, country);

        var reportPath = Path.ChangeExtension(file, ".validation.txt");
        ReportWriter.Write(remainingErrors, reportPath);

        int remainingErrorCount = remainingErrors.Count(e => e.Severity == Severity.Error);
        int fixableCount        = remainingErrors.Count(e => e.Severity == Severity.Fixable);

        Console.WriteLine();
        if (remainingErrorCount == 0)
        {
            Console.WriteLine($"  ✓ Source file is structurally clean.");
            if (fixableCount > 0)
                Console.WriteLine($"  ⚒ {fixableCount} fixable issue(s) remain — run 'fix' next.");
        }
        else
        {
            await Console.Error.WriteLineAsync($"  ✗ {remainingErrorCount} structural error(s) still remain.");
            await Console.Error.WriteLineAsync($"    This may indicate rows whose errors span multiple zips.");
            await Console.Error.WriteLineAsync($"    See: {reportPath}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  1. Review {extractedFile} and correct the records manually.");
        Console.WriteLine($"  2. Run: fix {file} --country {country.ToUpperInvariant()}");
        Console.WriteLine($"  3. Once clean, manually merge the corrected records back into:");
        Console.WriteLine($"     {file}");

        return 0;
    }

    // -------------------------------------------------------------------------

    private static void PrintUsage() =>
        Console.WriteLine(
            "Usage: countrydatatools extractissues <file.csv> --country XX [--out extracted.csv]");
}
