using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.DSV;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Pipeline;
using ZipPostLookup.CountryDataTools.Reporting;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools validate &lt;file.csv&gt; --country XX [--report errors.txt] [--no-prompts]
///
/// Validates a candidate CSV then guides the user through fix and extract steps
/// interactively. Each step shows what will happen and asks for confirmation before
/// making any changes to the file.
///
/// Interactive flow:
///   1. Validate  → display report
///   2. Prompt    → Fix? [y/n/i]   (skipped if no fixable issues)
///   3. Prompt    → Extract structural errors? [y/n/i]  (skipped if no errors)
///   4. Done
///
/// Pass --no-prompts to skip all prompts and apply fix + extract automatically.
/// This is equivalent to the old separate: validate → fix → extractissues sequence.
/// </summary>
public static class ValidateCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        var file      = args.Length > 0 ? args[0] : "";
        var country   = args.OptionValue("--country", rejectFlagValue: true) ?? "";
        var report    = args.OptionValue("--report");
        var noPrompts = args.HasFlag("--no-prompts");

        if (string.IsNullOrEmpty(file)) { PrintUsage(); return 2; }

        if (!File.Exists(file))
        {
            await Console.Error.WriteLineAsync($"File not found: {file}");
            return 2;
        }

        if (!CommandArgs.ResolveCountry(file, country, out country)) return 2;

        // =====================================================================
        // Step 1 — Validate
        // =====================================================================

        Console.WriteLine($"Validating {file} [{country.ToUpperInvariant()}]…");
        Console.WriteLine();

        var (rows, headerOk, missingCols) = CsvReader.Read(file);
        Console.WriteLine($"  Rows read : {rows.Count:N0}");

        var errors = ValidationRules.Validate(rows, headerOk, missingCols, country);

        int errorCount = errors.Count(e => e.Severity == Severity.Error);
        int fixableCount = errors.Count(e => e.Severity == Severity.Fixable);
        int warningCount = errors.Count(e => e.Severity == Severity.Warning);

        // Write report file
        var reportPath = report ?? Path.ChangeExtension(file, ".validation.txt");
        ReportWriter.Write(errors, reportPath);

        // Display summary
        Console.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Validation Report[/]").LeftJustified());

        if (errorCount == 0 && fixableCount == 0 && warningCount == 0)
        {
            AnsiConsole.MarkupLine("  [green]✓ No issues found.[/]");
        }
        else
        {
            if (errorCount   > 0) AnsiConsole.MarkupLine($"  [red]✗ Errors   : {errorCount,4}  (structural — require manual fix or extract)[/]");
            if (fixableCount > 0) AnsiConsole.MarkupLine($"  [yellow]⚒ Fixable  : {fixableCount,4}  (resolved automatically by fix)[/]");
            if (warningCount > 0) AnsiConsole.MarkupLine($"  [darkyellow]⚠ Warnings : {warningCount,4}  (advisory)[/]");

            // Show first few errors so user sees what's wrong without opening the report
            var topErrors = errors.Where(e => e.Severity == Severity.Error).Take(5).ToList();
            if (topErrors.Count > 0)
            {
                Console.WriteLine();
                foreach (var e in topErrors)
                {
                    AnsiConsole.MarkupLine($"  [red][[ERROR]] zip:{Markup.Escape(e.Zip)}  field:{Markup.Escape(e.Field)}  {e.ErrorType}[/]");
                }
                if (errorCount > 5)
                    AnsiConsole.MarkupLine($"  [grey]… and {errorCount - 5} more — see {Markup.Escape(reportPath)}[/]");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  Full report : {reportPath}");
        Console.WriteLine();

        // If header is broken nothing else can run
        if (!headerOk)
        {
            AnsiConsole.MarkupLine("  [red]✗ Header errors prevent fix and extract. Correct the file manually.[/]");
            return 1;
        }

        // =====================================================================
        // Step 2 — Fix
        // =====================================================================

        if (fixableCount == 0)
        {
            AnsiConsole.MarkupLine("  [green]✓ No fixable issues — skipping fix.[/]");
        }
        else
        {
            AnsiConsole.Write(new Rule());
            bool runFix = noPrompts || Prompt(
                $"Fix {fixableCount} fixable issue(s)?",
                fixInfo: """
                  Fix will automatically resolve:
                    · Whitespace trimming on all fields
                    · Boolean normalisation  (True/False → true/false)
                    · Title-casing Name and state names
                    · Timezone resolution from Windows names to IANA
                    · Duplicate zip+Name removal
                    · Setting IsDefault=true where no default exists for a zip
                  Structural errors are NOT fixed — use Extract for those.
                  The file is overwritten in place; a backup is not created automatically.
                """);

            if (runFix)
            {
                Console.WriteLine();
                Console.WriteLine($"  Fixing {file}…");

                var (fixedRows, fixLog) = Fixer.Fix(rows, country);

                var outPath = file;
                CsvWriter.Write(fixedRows, outPath);

                int fixedCount = fixLog.Count;
                AnsiConsole.MarkupLine($"  [green]✓ Fix complete — {fixedCount} row(s) updated.[/]");

                // Re-validate after fix so extract sees fresh counts
                var postFix = ValidationRules.Validate(fixedRows, true, Array.Empty<string>(), country);
                errorCount = postFix.Count(e => e.Severity == Severity.Error);
                rows = fixedRows;
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("  Fix skipped.");
                Console.WriteLine();
            }
        }

        // =====================================================================
        // Step 3 — Extract structural errors
        // =====================================================================

        if (errorCount == 0)
        {
            AnsiConsole.MarkupLine("  [green]✓ No structural errors — skipping extract.[/]");
            Console.WriteLine();
        }
        else
        {
            AnsiConsole.Write(new Rule());

            var extractedPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".",
                Path.GetFileNameWithoutExtension(file) + "-extracted-records.csv");

            bool runExtract = noPrompts || Prompt(
                $"Extract {errorCount} structural error row(s)?",
                fixInfo: $"""
                  Extract will remove {errorCount} row(s) with structural errors from {Path.GetFileName(file)}.
                  You will be asked where to write them:
                    (1) DB only   — insert into codes.candidate with status='error' (requires workdb.json)
                    (2) File only — write to {Path.GetFileName(extractedPath)} for manual review
                    (3) Both      — insert into DB and write file
                  Fixable issues (duplicates, timezone etc.) are NOT extracted.
                """);

            if (runExtract)
            {
                Console.WriteLine();

                // Ask where to write extracted rows
                var destination = noPrompts ? "2" : PromptDestination(errorCount, extractedPath);

                var postErrors = ValidationRules.Validate(rows, true, Array.Empty<string>(), country);
                var errorRecords = postErrors
                    .Where(e => e.Severity == Severity.Error)
                    .Select(e => e.RecordNumber)
                    .ToHashSet();

                var cleanRows = rows.Where(r => !errorRecords.Contains(r.RecordNumber)).ToList();
                var extractedRows = rows.Where(r => errorRecords.Contains(r.RecordNumber)).ToList();

                // Always write the clean rows back to the source file
                CsvWriter.Write(cleanRows, file);

                // Write to DB
                if (destination is "1" or "3")
                {
                    try
                    {
                        var db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
                        var runId = await db.Runs.CreateRunAsync(country, Path.GetFileName(file));

                        var extractedCandidates = extractedRows
                            .Select(r => new CodesCandidate(country, r))
                            .ToList();
                        await db.Candidates.InsertBatchAsync(extractedCandidates);

                        // Mark all inserted rows as error status
                        using var conn = db.GetFactory().CreateConnection();
                        await Dapper.SqlMapper.ExecuteAsync(conn,
                            CommonQueries.MarkCandidatesAsError,
                            new { CountryId = country.ToUpperInvariant(), RunId = runId });

                        AnsiConsole.MarkupLine($"  [green]✓ {extractedRows.Count} row(s) inserted into codes.candidate (status=error)  run:{Markup.Escape(runId)}[/]");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"  [red]✗ DB write failed: {Markup.Escape(ex.Message)}[/]");
                        AnsiConsole.MarkupLine("  [yellow]Falling back to file output…[/]");
                        CsvWriter.Write(extractedRows, extractedPath);
                        AnsiConsole.MarkupLine($"  [green]✓ {extractedRows.Count} row(s) written to {Markup.Escape(extractedPath)}[/]");
                    }
                }

                // Write to file
                if (destination is "2" or "3")
                {
                    CsvWriter.Write(extractedRows, extractedPath);
                    AnsiConsole.MarkupLine($"  [green]✓ {extractedRows.Count} row(s) written to {Markup.Escape(extractedPath)}[/]");
                }

                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("  Extract skipped.");
                Console.WriteLine();
            }
        }

        // =====================================================================
        // Done
        // =====================================================================

        AnsiConsole.Write(new Rule());
        AnsiConsole.MarkupLine("  [green]Complete.[/]");

        if (errorCount == 0 && fixableCount == 0)
        {
            Console.WriteLine($"  {file} is ready for ingest.");
        }
        else if (errorCount > 0)
        {
            AnsiConsole.MarkupLine($"  [yellow]⚠ {errorCount} structural error(s) remain — fix manually or run extract.[/]");
        }

        Console.WriteLine();
        return errorCount > 0 ? 1 : 0;
    }

    // -------------------------------------------------------------------------
    // Prompt helper — [y/n/i]
    // -------------------------------------------------------------------------

    private static bool Prompt(string question, string fixInfo)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"  {question}")
                    .AddChoices("Yes", "No", "Show details"));

            if (choice == "Yes") return true;
            if (choice == "No")  return false;

            AnsiConsole.Write(new Panel(fixInfo.Trim()).BorderColor(Color.Grey));
        }
    }

    private static string PromptDestination(int count, string extractedPath)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"  Where should the {count} extracted row(s) be written?")
                .AddChoices(
                    "1 — DB only   (codes.candidate, status=error)",
                    $"2 — File only ({Path.GetFileName(extractedPath)})",
                    "3 — Both"));

        return choice[0].ToString(); // leading "1", "2", or "3"
    }


    private static void PrintUsage() =>
        Console.WriteLine(
            "Usage: countrydatatools validate <file.csv> --country XX [--report errors.txt] [--no-prompts]");
}