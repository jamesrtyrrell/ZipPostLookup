using ZipPostLookup.CountryDataTools.DSV;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Pipeline;
using ZipPostLookup.CountryDataTools.Reporting;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools fix &lt;file.csv&gt; --country XX [--out path.csv]
///
/// Blocks on Severity.Error (structural issues a machine cannot fix).
/// Severity.Fixable issues (duplicates, booleans, casing, timezone conversion)
/// are resolved automatically. Output writes back to the same file by default.
/// The post-fix report lists what was fixed and what (if anything) still needs
/// manual attention.
/// </summary>
public static class FixCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        if (!TryParseArgs(args, out var file, out var country, out var outFile))
        {
            PrintUsage();
            return 2;
        }

        if (!File.Exists(file))
        {
            await Console.Error.WriteLineAsync($"File not found: {file}");
            return 2;
        }

        // --- 1. Validate — block on structural errors ---
        if (!CommandArgs.ResolveCountry(file, country, out country)) return 2;

        Console.WriteLine($"Pre-fix validation of {file} [{country.ToUpperInvariant()}]…");

        var (rows, headerOk, missingCols) = CsvReader.Read(file);
        Console.WriteLine($"  Rows read: {rows.Count:N0}");

        var errors = ValidationRules.Validate(rows, headerOk, missingCols, country);

        int errorCount   = errors.Count(e => e.Severity == Severity.Error);
        int fixableCount = errors.Count(e => e.Severity == Severity.Fixable);
        int warningCount = errors.Count(e => e.Severity == Severity.Warning);

        if (errorCount > 0)
        {
            // Write the full pre-fix report so the user can see everything at once
            var preReportPath = Path.ChangeExtension(file, ".validation.txt");
            ReportWriter.Write(errors, preReportPath);
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync($"  ✗ {errorCount} structural error(s) must be fixed manually before 'fix' can run.");
            if (fixableCount > 0)
                await Console.Error.WriteLineAsync($"  ⚒ {fixableCount} fixable issue(s) will be resolved automatically once the errors above are corrected.");
            await Console.Error.WriteLineAsync($"  Fix the errors in the file, then re-run: fix {file} --country {country.ToUpperInvariant()}");
            await Console.Error.WriteLineAsync($"  Full report: {preReportPath}");
            return 1;
        }

        if (fixableCount > 0)
        {
            Console.WriteLine($"  ⚒ {fixableCount} fixable issue(s) will be resolved automatically.");
        }
        if (warningCount > 0)
        {
            Console.WriteLine($"  ⚠ {warningCount} warning(s) noted.");
        }
        Console.WriteLine("  No structural errors — proceeding with fixes.");

        // --- 2. Apply fixes ---
        Console.WriteLine("Applying fixes…");
        var (fixedRows, fixLog) = Fixer.Fix(rows, country);

        int removedRows  = rows.Count - fixedRows.Count;
        int fieldChanges = fixLog.Count(f => f.Field != "row");

        if (fieldChanges > 0)
        {
            Console.WriteLine($"  Field changes      : {fieldChanges:N0}");
        }
        if (removedRows > 0)
        {
            Console.WriteLine($"  Duplicate rows removed: {removedRows:N0}");
        }

        foreach (var fix in fixLog)
        {
            if (fix.Field == "row")
                Console.WriteLine(
                    $"    record:{fix.RecordNumber,-6} zip:{fix.Zip,-12} REMOVED — {fix.Was}");
            else
                Console.WriteLine(
                    $"    record:{fix.RecordNumber,-6} zip:{fix.Zip,-12} field:{fix.Field,-12} " +
                    $"\"{fix.Was}\" → \"{fix.Now}\"");
        }

        // --- 3. Write fixed CSV back to the same file (or --out path) ---
        outFile ??= file;   // default: overwrite in place
        CsvWriter.Write(fixedRows, outFile);
        Console.WriteLine($"  Updated: {outFile}");

        // --- 4. Post-fix validation + report that includes what was fixed ---
        Console.WriteLine("Post-fix validation…");
        var (postRows, postHeaderOk, postMissingCols) = CsvReader.Read(outFile);
        var postErrors = ValidationRules.Validate(postRows, postHeaderOk, postMissingCols, country);
        int postErrorCount = postErrors.Count(e => e.Severity == Severity.Error);

        var postReportPath = Path.ChangeExtension(outFile, ".validation.txt");
        ReportWriter.WritePostFix(postErrors, fixLog, postReportPath);

        if (postErrorCount > 0)
        {
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync($"  ✗ {postErrorCount} structural error(s) remain — manual correction needed.");
            await Console.Error.WriteLineAsync($"  Report: {postReportPath}");
            return 1;
        }

        Console.WriteLine("  ✓ File is clean.");
        return 0;
    }

    // -------------------------------------------------------------------------

    private static bool TryParseArgs(
        string[] args,
        out string file,
        out string country,
        out string? outFile)
    {
        file    = "";
        country = "";
        outFile = null;

        if (args.Length < 1) return false;
        file = args[0];

        for (int i = 1; i < args.Length - 1; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--country": country = args[++i]; break;
                case "--out":     outFile = args[++i]; break;
            }
        }

        return !string.IsNullOrEmpty(file);
    }

    private static void PrintUsage() =>
        Console.WriteLine(
            "Usage: countrydatatools fix <file.csv> --country XX [--out path.csv]");
}
