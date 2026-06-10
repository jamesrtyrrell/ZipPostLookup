using Spectre.Console;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Validation.Csv;

public static class ReportWriter
{
    /// <summary>UTF-8 with BOM — unambiguous for editors and Excel.</summary>
    public static readonly System.Text.Encoding Utf8Bom =
        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Standard validation report — written by 'validate' and by 'fix' when it
    /// cannot proceed due to structural errors.
    /// </summary>
    public static void Write(IReadOnlyList<ValidationError> errors, string reportPath)
    {
        int errorCount = errors.Count(e => e.Severity == Severity.Error);
        int fixableCount = errors.Count(e => e.Severity == Severity.Fixable);
        int warningCount = errors.Count(e => e.Severity == Severity.Warning);

        using (var w = new StreamWriter(reportPath, append: false, encoding: Utf8Bom))
        {
            w.WriteLine("# ZipPostLookup.CountryDataTools — Validation Report");
            w.WriteLine($"# Generated : {DateTime.UtcNow:u}");
            w.WriteLine($"# Errors    : {errorCount}   (structural — require manual fix)");
            w.WriteLine($"# Fixable   : {fixableCount}  (resolved automatically by 'fix' command)");
            w.WriteLine($"# Warnings  : {warningCount}  (advisory)");
            w.WriteLine();

            if (errors.Count == 0)
            {
                w.WriteLine("No issues found.");
            }
            else
            {
                foreach (var e in errors.OrderBy(e => e.RecordNumber))
                    w.WriteLine($"[{SeverityTag(e.Severity)}] {e}");
            }
        }

        PrintConsoleSummary(errorCount, fixableCount, warningCount, reportPath);
    }

    /// <summary>
    /// Post-fix report — written by 'fix' after applying changes.
    /// Shows what was fixed, then any remaining issues that still need attention.
    /// </summary>
    public static void WritePostFix(
        IReadOnlyList<ValidationError> remaining,
        IReadOnlyList<Fixer.FixRecord> fixLog,
        string reportPath)
    {
        int errorCount = remaining.Count(e => e.Severity == Severity.Error);
        int warningCount = remaining.Count(e => e.Severity == Severity.Warning);

        int rowsRemoved = fixLog.Count(f => f.Field == "row");
        int fieldChanges = fixLog.Count(f => f.Field != "row");

        using (var w = new StreamWriter(reportPath, append: false, encoding: Utf8Bom))
        {
            w.WriteLine("# ZipPostLookup.CountryDataTools — Post-Fix Report");
            w.WriteLine($"# Generated    : {DateTime.UtcNow:u}");
            w.WriteLine($"# Field changes: {fieldChanges}");
            w.WriteLine($"# Rows removed : {rowsRemoved}  (duplicates)");
            w.WriteLine($"# Errors remain: {errorCount}   (require manual fix)");
            w.WriteLine($"# Warnings     : {warningCount}  (advisory)");

            // --- What was fixed ---
            w.WriteLine();
            w.WriteLine("## Changes applied");
            w.WriteLine();
            if (fixLog.Count == 0)
            {
                w.WriteLine("  (no changes)");
            }
            else
            {
                foreach (var fix in fixLog.OrderBy(f => f.RecordNumber))
                {
                    if (fix.Field == "row")
                        w.WriteLine($"  [REMOVED] record:{fix.RecordNumber,-6} zip:{fix.Zip,-12} {fix.Was}");
                    else
                        w.WriteLine($"  [FIXED  ] record:{fix.RecordNumber,-6} zip:{fix.Zip,-12} field:{fix.Field,-12} \"{fix.Was}\" → \"{fix.Now}\"");
                }
            }

            // --- What still needs attention ---
            w.WriteLine();
            w.WriteLine("## Remaining issues");
            w.WriteLine();
            if (remaining.Count == 0)
            {
                w.WriteLine("  None — file is clean.");
            }
            else
            {
                foreach (var e in remaining.OrderBy(e => e.RecordNumber))
                    w.WriteLine($"  [{SeverityTag(e.Severity)}] {e}");
            }
        }

        Console.WriteLine();
        if (errorCount == 0 && warningCount == 0)
            AnsiConsole.MarkupLine("  [green]✓ File is clean after fixing.[/]");
        else
        {
            if (errorCount   > 0) AnsiConsole.MarkupLine($"  [red]✗ {errorCount} error(s) remain — manual fix required[/]");
            if (warningCount > 0) AnsiConsole.MarkupLine($"  [yellow]⚠ {warningCount} warning(s)[/]");
        }
        Console.WriteLine($"  Report: {reportPath}");
    }

    // -------------------------------------------------------------------------

    private static void PrintConsoleSummary(
        int errorCount, int fixableCount, int warningCount, string reportPath)
    {
        Console.WriteLine();
        if (errorCount == 0 && fixableCount == 0 && warningCount == 0)
        {
            AnsiConsole.MarkupLine("  [green]✓ Validation passed — no issues found.[/]");
        }
        else
        {
            if (errorCount   > 0) AnsiConsole.MarkupLine($"  [red]✗ {errorCount} error(s) — manual fix required[/]");
            if (fixableCount > 0) AnsiConsole.MarkupLine($"  [cyan]⚒ {fixableCount} fixable issue(s) — run 'fix' to resolve[/]");
            if (warningCount > 0) AnsiConsole.MarkupLine($"  [yellow]⚠ {warningCount} warning(s) — advisory[/]");
        }
        Console.WriteLine($"  Report: {reportPath}");
    }

    private static string SeverityTag(Severity s) => s switch
    {
        Severity.Error => "ERROR  ",
        Severity.Fixable => "FIXABLE",
        Severity.Warning => "WARNING",
        _ => "UNKNOWN"
    };

}