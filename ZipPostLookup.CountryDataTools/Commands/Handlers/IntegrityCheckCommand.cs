using System.Text;
using Dapper;
using Spectre.Console;
using ZipPostLookup.Core;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Models.Commands;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Validation;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools integrity --country XX --tests 5000 [--output report.md]
///
/// Data integrity check — verifies that ZipPostLookup returns the correct name,
/// timezone, and admin info for a random sample of codes drawn from the curated
/// reference data in the working database.
///
/// This is NOT a benchmark or a unit test. Its purpose is to catch mismatches
/// between what the DB says is correct (source of truth) and what the library
/// actually returns at runtime. Useful after re-exporting, re-compressing, or
/// modifying the embedded CSV data files.
///
/// Sampling strategy:
///   GetByCode / GetByZip  — sample the specific row the library will return for each
///                           zip: the IsDefault=1 row whose Name is alphabetically last
///                           among all IsDefault=1 rows for that zip. This matches
///                           ZipPostRegistry's last-write-wins index behaviour (the CSV
///                           is sorted by zip, name asc — so the last IsDefault=true row
///                           loaded is the alphabetically last name, and that is what
///                           GetByCode returns). Comparing against any other IsDefault=1
///                           row would produce false failures on countries like MX where
///                           many zips have multiple IsDefault=1 rows in the DB.
///   GetAllByCode (default) — same sample: verify default entry fields are correct.
///   GetAllByCode (non-default) — separate sample of IsDefault=0 rows: the name must
///                           appear somewhere in the full result list.
///
/// --tests N controls the number of *unique zip codes* tested for the default checks.
///
/// Output:
///   · Console progress (one dot per 100 codes checked, no raw errors/warnings)
///   · A Markdown summary report: {cc}-integrity-{date}.md
/// </summary>
public static class IntegrityCheckCommand
{
    // =========================================================================
    // Entry point
    // =========================================================================

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        if (!TryParseArgs(args, out var country, out var testCount, out var output, out var all))
        {
            PrintUsage();
            return 2;
        }

        WorkDbContext db;
        try
        {
            db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"  ✗ DB connection failed: {ex.Message}");
            return 1;
        }

        if (all)
            return await RunAllAsync(db, testCount);

        return await RunForCountryAsync(db, country.ToUpperInvariant(), testCount, output);
    }

    private static async Task<int> RunAllAsync(WorkDbContext db, int testCount)
    {
        var exitCode = 0;
        foreach (var cc in new[] { "US", "CA", "MX" })
        {
            Console.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]{cc}[/]").LeftJustified());
            var result = await RunForCountryAsync(db, cc, testCount, null);
            if (result != 0) exitCode = result;
        }
        return exitCode;
    }

    private static async Task<int> RunForCountryAsync(
        WorkDbContext db, string cc, int testCount, string? output)
    {
        using var conn = db.GetFactory().CreateConnection();

        // ── Guard: country must exist and be curated ──────────────────────────
        var countryInfo = await conn.QueryFirstOrDefaultAsync<DataCountryInfo>(
            CommonQueries.GetCountryInfoById,
            new { CountryId = cc });

        if (countryInfo is null || string.IsNullOrEmpty(countryInfo.CountryId))
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ Country '{cc}' not found in data.country_info.");
            return 1;
        }

        if (!countryInfo.DataCurated)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ {cc} is not fully curated (data_curated = 0). " +
                $"Run the curation pipeline before running an integrity check.");
            return 1;
        }

        // ── Load the ZipPostLookup registry for this country ──────────────────
        AnsiConsole.MarkupLine($"  [grey]Loading ZipPostLookup registry for {Markup.Escape(cc)}...[/]");
        ZipPostRegistry registry;
        try
        {
            registry = new ZipPostRegistry((CountryCode)cc);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ Failed to load ZipPostLookup registry for {cc}: {ex.Message}");
            return 1;
        }

        // ── Fetch count of distinct zip codes ─────────────────────────────────
        // Cap --tests against distinct zip count, not row count, because we
        // sample one representative row per zip for the default checks.
        var totalDistinctZips = await conn.ExecuteScalarAsync<int>(
            CommonQueries.GetDistinctCodeCount,
            new { CountryId = cc });

        var totalCuratedRows = await conn.ExecuteScalarAsync<int>(
            CommonQueries.GetCuratedReferenceCount,
            new { CountryId = cc });

        if (totalDistinctZips == 0)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ No curated rows found in data.reference for {cc}.");
            return 1;
        }

        var actualTestCount = Math.Min(testCount, totalDistinctZips);

        IntegrityDisplay.PrintHeader(
            cc, countryInfo.CountryName,
            totalCuratedRows, totalDistinctZips, actualTestCount, testCount);

        // ── Sample default rows ───────────────────────────────────────────────
        // For each sampled zip, select the IsDefault=1 row with the alphabetically
        // last Name. This is the exact row ZipPostRegistry indexes as the default
        // entry (last-write-wins, CSV sorted by zip + name asc).
        AnsiConsole.MarkupLine($"  [grey]Sampling {actualTestCount:N0} representative rows...[/]");
        var defaultRows = await SampleDefaultRowsAsync(conn, cc, actualTestCount);

        if (defaultRows.Count == 0)
        {
            await Console.Error.WriteLineAsync("  ✗ Default-row sampling returned 0 rows.");
            return 1;
        }

        // ── Sample non-default rows ───────────────────────────────────────────
        // Exclude special-domain codes (APO/FPO/territory) — they have no fixed
        // coordinates and no geographic integrity checks apply to them.
        var rules = CountryRulesFactory.For(cc);
        var nonDefaultRows = (await SampleNonDefaultRowsAsync(conn, cc, actualTestCount))
            .Where(r => !rules.IsCoordResolutionSkipped(r.ZpCode))
            .ToList();

        Console.WriteLine($"  Default sample     : {defaultRows.Count:N0}");
        Console.WriteLine($"  Non-default sample : {nonDefaultRows.Count:N0}");
        Console.WriteLine();
        Console.Write("  Running checks");

        // ── Run integrity checks ──────────────────────────────────────────────
        var results = RunChecks(registry, defaultRows, nonDefaultRows);

        Console.WriteLine();  // newline after progress dots
        Console.WriteLine();

        // ── Summary to console ────────────────────────────────────────────────
        var summary = new IntegrityCheckSummary(
            DefaultTested:                    defaultRows.Count,
            NonDefaultTested:                 nonDefaultRows.Count,
            GetByCodeAttempts:                results.GetByCodeAttempts,
            GetByCodeFailures:                results.GetByCodeFailures,
            GetByZipAttempts:                 results.GetByZipAttempts,
            GetByZipFailures:                 results.GetByZipFailures,
            GetAllByCodeAttempts:             results.GetAllByCodeAttempts,
            GetAllByCodeFailures:             results.GetAllByCodeFailures,
            GetAllByCodeNonDefaultAttempts:   results.GetAllByCodeNonDefaultAttempts,
            GetAllByCodeNonDefaultFailures:   results.GetAllByCodeNonDefaultFailures,
            TotalFailures:                    results.TotalFailures,
            UniqueFailed:                     results.UniqueFailed,
            UniqueDefaultFailed:              results.UniqueDefaultFailed,
            HasFailures:                      results.HasFailures);
        IntegrityDisplay.PrintSummary(cc, summary);

        // ── Write Markdown report ─────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(output))
        {
            var dataAnalysisPath = Path.Combine(Directory.GetCurrentDirectory(), "DataAnalysis");
            if (!Directory.Exists(dataAnalysisPath))
            {
                Directory.CreateDirectory(dataAnalysisPath);
            }

            output = Path.Combine(dataAnalysisPath,
                $"{cc.ToLowerInvariant()}-integrity-{DateTime.UtcNow:yyyyMMdd}.md");
        }

        var report = BuildMarkdownReport(
            cc, countryInfo.CountryName,
            defaultRows.Count, nonDefaultRows.Count,
            totalDistinctZips, totalCuratedRows,
            results);
        await File.WriteAllTextAsync(output, report);

        Console.WriteLine($"  ✓ Report written to: {output}");
        Console.WriteLine();

        return results.HasFailures ? 1 : 0;
    }

    // =========================================================================
    // Sampling
    // =========================================================================

    /// <summary>
    /// Randomly samples <paramref name="count"/> distinct zip codes, and for each
    /// returns the IsDefault=1 row with the alphabetically last Name.
    ///
    /// This is the specific row ZipPostRegistry will return from GetByCode —
    /// the CSV is sorted by zip + name asc, so the last IsDefault=true row
    /// loaded for a zip is the alphabetically last name (last-write-wins).
    ///
    /// Uses a two-step query: first draw a random set of distinct zips via NEWID(),
    /// then join back to pick the correct representative row per zip.
    /// </summary>
    private static async Task<List<ReferenceRowFull>> SampleDefaultRowsAsync(
        System.Data.IDbConnection conn,
        string cc,
        int count)
    {
        var rows = await conn.QueryAsync<ReferenceRowFull>(
            CommonQueries.GetTestDataSet,
            new { CountryId = cc, Count = count });

        return rows.ToList();
    }

    /// <summary>
    /// Randomly samples up to <paramref name="count"/> rows where IsDefault = 0.
    /// Used only for the GetAllByCode name-presence check.
    /// Returns an empty list if no non-default rows exist.
    /// </summary>
    private static async Task<List<ReferenceRowFull>> SampleNonDefaultRowsAsync(
        System.Data.IDbConnection conn,
        string cc,
        int count)
    {
        var rows = await conn.QueryAsync<ReferenceRowFull>(
            CommonQueries.GetNonDefaultTestDataSet,
            new { CountryId = cc, Count = count });

        return rows.ToList();
    }

    // =========================================================================
    // Checks
    // =========================================================================

    private static CheckResults RunChecks(
        ZipPostRegistry registry,
        List<ReferenceRowFull> defaultRows,
        List<ReferenceRowFull> nonDefaultRows)
    {
        var results = new CheckResults();
        var dot = 0;

        // Default rows: all three checks
        foreach (var row in defaultRows)
        {
            dot++;
            if (dot % 100 == 0) { Console.Write('.'); }

            CheckGetByCode(registry, row, results);
            CheckGetByZip(registry, row, results);
            CheckGetAllByCodeDefault(registry, row, results);
        }

        // Non-default rows: GetAllByCode name-presence only
        foreach (var row in nonDefaultRows)
        {
            dot++;
            if (dot % 100 == 0) { Console.Write('.'); }

            CheckGetAllByCodeNonDefault(registry, row, results);
        }

        return results;
    }

    private static void CheckGetByCode(
        ZipPostRegistry registry, ReferenceRowFull row, CheckResults results)
    {
        results.GetByCodeAttempts++;
        var entry = registry.GetByCode(row.ZpCode);

        if (entry is null)
        {
            results.AddFailure(CheckKind.GetByCode, row.ZpCode, "returned null (miss)");
            return;
        }

        CheckEntry(CheckKind.GetByCode, row, entry, results);
    }

    private static void CheckGetByZip(
        ZipPostRegistry registry, ReferenceRowFull row, CheckResults results)
    {
        results.GetByZipAttempts++;
        var entry = registry.GetByZip(row.ZpCode);

        if (entry is null)
        {
            results.AddFailure(CheckKind.GetByZip, row.ZpCode, "returned null (miss)");
            return;
        }

        CheckEntry(CheckKind.GetByZip, row, entry, results);
    }

    /// <summary>
    /// GetAllByCode check for a default row: the result list must contain a default entry
    /// and that entry's fields must match the DB row.
    /// </summary>
    private static void CheckGetAllByCodeDefault(
        ZipPostRegistry registry, ReferenceRowFull row, CheckResults results)
    {
        results.GetAllByCodeAttempts++;
        var entries = registry.GetAllByCode(row.ZpCode);

        if (entries.Count == 0)
        {
            results.AddFailure(CheckKind.GetAllByCode, row.ZpCode, "returned empty list (miss)");
            return;
        }

        var defaultEntry = entries.FirstOrDefault(e => e.IsDefault);
        if (defaultEntry is null)
        {
            results.AddFailure(CheckKind.GetAllByCode, row.ZpCode,
                $"no IsDefault=true entry in {entries.Count} result(s)");
            return;
        }

        CheckEntry(CheckKind.GetAllByCode, row, defaultEntry, results);
    }

    /// <summary>
    /// GetAllByCode check for a non-default row: the name must appear somewhere
    /// in the result list (any entry, not just the default).
    /// </summary>
    private static void CheckGetAllByCodeNonDefault(
        ZipPostRegistry registry, ReferenceRowFull row, CheckResults results)
    {
        results.GetAllByCodeNonDefaultAttempts++;
        var entries = registry.GetAllByCode(row.ZpCode);

        if (entries.Count == 0)
        {
            results.AddFailure(CheckKind.GetAllByCodeNonDefault, row.ZpCode,
                "returned empty list (miss)");
            return;
        }

        var match = entries.FirstOrDefault(e =>
            string.Equals(e.PlaceName, row.PlaceName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            results.AddFailure(CheckKind.GetAllByCodeNonDefault, row.ZpCode,
                $"non-default name '{row.PlaceName}' not found in {entries.Count} result(s)");
        }
    }

    private static void CheckEntry(
        CheckKind kind, ReferenceRowFull row, CodeEntry entry, CheckResults results)
    {
        // PlaceName
        if (!string.Equals(entry.PlaceName, row.PlaceName, StringComparison.OrdinalIgnoreCase))
        {
            results.AddFailure(kind, row.ZpCode,
                $"name mismatch — expected '{row.PlaceName}', got '{entry.PlaceName}'");
        }

        // Timezone
        if (!string.Equals(entry.Timezone, row.Timezone, StringComparison.OrdinalIgnoreCase))
        {
            results.AddFailure(kind, row.ZpCode,
                $"timezone mismatch — expected '{row.Timezone}', got '{entry.Timezone}'");
        }

        // Admin1 value (only if the DB row has admin info)
        if (!string.IsNullOrWhiteSpace(row.Admin1) && row.Admin1 != "---")
        {
            if (!string.Equals(entry.Admin1, row.Admin1, StringComparison.OrdinalIgnoreCase))
            {
                results.AddFailure(kind, row.ZpCode,
                    $"admin1 value mismatch — expected '{row.Admin1}', got '{entry.Admin1 ?? "(null)"}'");
            }
        }

        // Admin1 code
        if (!string.IsNullOrWhiteSpace(row.Admin1Code) && row.Admin1Code != "---")
        {
            if (!string.Equals(entry.Admin1Code, row.Admin1Code, StringComparison.OrdinalIgnoreCase))
            {
                results.AddFailure(kind, row.ZpCode,
                    $"admin1 code mismatch — expected '{row.Admin1Code}', got '{entry.Admin1Code ?? "(null)"}'");
            }
        }
    }

    // =========================================================================
    // Markdown report
    // =========================================================================

    private static string BuildMarkdownReport(
        string cc,
        string countryName,
        int defaultTested,
        int nonDefaultTested,
        int totalDistinctZips,
        int totalCuratedRows,
        CheckResults results)
    {
        var sb           = new StringBuilder();
        var now          = DateTime.UtcNow;
        var uniqueFailed = results.UniqueFailed;
        var cleanDefault = defaultTested - results.UniqueDefaultFailed;
        var passRate     = defaultTested > 0 ? (double)cleanDefault / defaultTested * 100 : 0;
        var coverage     = totalDistinctZips > 0 ? (double)defaultTested / totalDistinctZips * 100 : 0;
        var status       = results.HasFailures ? "❌ FAILURES FOUND" : "✅ PASSED";

        sb.AppendLine($"# {cc} — ZipPostLookup Data Integrity Report");
        sb.AppendLine();
        sb.AppendLine($"| | |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| **Country** | {cc} — {countryName} |");
        sb.AppendLine($"| **Date** | {now:yyyy-MM-dd HH:mm} UTC |");
        sb.AppendLine($"| **Result** | {status} |");
        sb.AppendLine($"| **Default codes tested** | {defaultTested:N0} of {totalDistinctZips:N0} distinct zip codes ({coverage:F1}% coverage) |");
        sb.AppendLine($"| **Non-default rows tested** | {nonDefaultTested:N0} (GetAllByCode name-presence only) |");
        sb.AppendLine($"| **Pass rate** | {passRate:F2}% ({cleanDefault:N0} / {defaultTested:N0} default codes clean) |");
        sb.AppendLine($"| **Total failures** | {results.TotalFailures:N0} across {uniqueFailed:N0} unique code(s) |");
        sb.AppendLine();

        // ── Per-method summary ────────────────────────────────────────────────
        sb.AppendLine("## Per-Method Summary");
        sb.AppendLine();
        sb.AppendLine("| Method | Sample | Checks | Failures | Pass Rate |");
        sb.AppendLine("|---|---|---:|---:|---:|");

        AppendMethodRow(sb, "GetByCode",                  "Default only",  results.GetByCodeAttempts,               results.GetByCodeFailures);
        AppendMethodRow(sb, "GetByZip",                   "Default only",  results.GetByZipAttempts,                results.GetByZipFailures);
        AppendMethodRow(sb, "GetAllByCode (default)",     "Default only",  results.GetAllByCodeAttempts,            results.GetAllByCodeFailures);
        AppendMethodRow(sb, "GetAllByCode (non-default)", "Non-default",   results.GetAllByCodeNonDefaultAttempts,  results.GetAllByCodeNonDefaultFailures);

        sb.AppendLine();

        // ── Failure breakdown by category ─────────────────────────────────────
        if (results.HasFailures)
        {
            sb.AppendLine("## Failure Breakdown");
            sb.AppendLine();

            var byCategory = results.Failures
                .GroupBy(f => f.Category)
                .OrderByDescending(g => g.Count())
                .ToList();

            sb.AppendLine("| Failure Category | Count |");
            sb.AppendLine("|---|---:|");
            foreach (var group in byCategory)
            {
                sb.AppendLine($"| {group.Key} | {group.Count():N0} |");
            }

            sb.AppendLine();

            sb.AppendLine("## Sample Failures *(up to 25)*");
            sb.AppendLine();
            sb.AppendLine("| Method | Code | Detail |");
            sb.AppendLine("|---|---|---|");

            foreach (var failure in results.Failures.Take(25))
            {
                var detail = failure.Detail.Replace("|", "\\|");
                sb.AppendLine($"| {failure.Kind} | `{failure.Code}` | {detail} |");
            }

            if (results.Failures.Count > 25)
            {
                sb.AppendLine();
                sb.AppendLine(
                    $"> **{results.Failures.Count - 25:N0} additional failures not shown.** " +
                    $"Re-run with a larger `--tests` value or inspect the DB directly.");
            }

            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## Failures");
            sb.AppendLine();
            sb.AppendLine("No failures detected. All sampled codes returned correct name, timezone, and admin data.");
            sb.AppendLine();
        }

        // ── What was checked ──────────────────────────────────────────────────
        sb.AppendLine("## What Was Checked");
        sb.AppendLine();
        sb.AppendLine("Each sampled **default** code was verified against the curated rows in `data.reference`:");
        sb.AppendLine();
        sb.AppendLine("- **GetByCode** — name, timezone, admin1 value, admin1 code");
        sb.AppendLine("- **GetByZip** — same fields (alias parity with GetByCode)");
        sb.AppendLine("- **GetAllByCode** — default entry present, default entry fields correct");
        sb.AppendLine();
        sb.AppendLine("Each sampled **non-default** row was verified separately:");
        sb.AppendLine();
        sb.AppendLine("- **GetAllByCode** — the non-default name must appear somewhere in the result list");
        sb.AppendLine();
        sb.AppendLine("> For each zip the **representative default row** is the `IsDefault=1` row whose");
        sb.AppendLine("> `Name` is alphabetically last. This matches `ZipPostRegistry`'s last-write-wins");
        sb.AppendLine("> index behaviour: the CSV is sorted by zip + name ascending, so the last");
        sb.AppendLine("> `IsDefault=true` row processed is the alphabetically last name — and that is");
        sb.AppendLine("> the entry `GetByCode` / `GetByZip` returns. Sampling any other `IsDefault=1`");
        sb.AppendLine("> row would produce false failures on countries like MX where many zips have");
        sb.AppendLine("> multiple `IsDefault=1` rows in the database.");
        sb.AppendLine();
        sb.AppendLine("> This is a **data integrity** check, not a benchmark or unit test.");
        sb.AppendLine("> Its purpose is to confirm that the embedded CSV data loaded by ZipPostLookup");
        sb.AppendLine("> matches the curated source-of-truth in the working database.");

        return sb.ToString();
    }

    private static void AppendMethodRow(
        StringBuilder sb, string method, string sample, int attempts, int failures)
    {
        var passRate = attempts > 0 ? (double)(attempts - failures) / attempts * 100 : 100.0;
        sb.AppendLine($"| {method} | {sample} | {attempts:N0} | {failures:N0} | {passRate:F2}% |");
    }

    // =========================================================================
    // Arg parsing
    // =========================================================================

    private static bool TryParseArgs(
        string[] args,
        out string country,
        out int testCount,
        out string? output,
        out bool all)
    {
        country   = "";
        testCount = 1000;
        output    = null;
        all       = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--country" when i + 1 < args.Length && !args[i + 1].StartsWith('-'):
                    country = args[++i];
                    break;
                case "--all":
                    all = true;
                    break;
                case "--tests" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out testCount) || testCount < 1)
                    {
                        Console.Error.WriteLine("  --tests must be a positive integer.");
                        return false;
                    }
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
            }
        }

        return all || !string.IsNullOrWhiteSpace(country);
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools integrity --country XX [--tests N] [--output report.md]
                or countrydatatools integrity --all        [--tests N]

              --country XX   Country to test (US / CA / MX)
              --all          Run for all pipeline countries (US, CA, MX) in sequence
              --tests N      Number of random zip codes to test (default: 1000)
              --output       Path for the Markdown report (default: {cc}-integrity-{date}.md)
            """);

    // =========================================================================
    // Helpers
    // =========================================================================

    // =========================================================================
    // Internal models
    // =========================================================================

    private enum CheckKind
    {
        GetByCode,
        GetByZip,
        GetAllByCode,
        GetAllByCodeNonDefault
    }

    private sealed class IntegrityFailure
    {
        public CheckKind Kind  { get; init; }
        public string    Code  { get; init; } = string.Empty;
        public string    Detail { get; init; } = string.Empty;

        public string Category => Detail switch
        {
            _ when Detail.StartsWith("name mismatch")     => "Name mismatch",
            _ when Detail.StartsWith("timezone mismatch") => "Timezone mismatch",
            _ when Detail.StartsWith("admin1 value")      => "Admin1 value mismatch",
            _ when Detail.StartsWith("admin1 code")       => "Admin1 code mismatch",
            _ when Detail.Contains("null")                => "Miss (null returned)",
            _ when Detail.Contains("empty")               => "Miss (empty list)",
            _ when Detail.Contains("IsDefault")           => "Missing default entry",
            _ when Detail.Contains("not found in")        => "Non-default name missing",
            _                                             => "Other"
        };
    }

    private sealed class CheckResults
    {
        public int GetByCodeAttempts               { get; set; }
        public int GetByZipAttempts                { get; set; }
        public int GetAllByCodeAttempts            { get; set; }
        public int GetAllByCodeNonDefaultAttempts  { get; set; }

        public int GetByCodeFailures               { get; private set; }
        public int GetByZipFailures                { get; private set; }
        public int GetAllByCodeFailures            { get; private set; }
        public int GetAllByCodeNonDefaultFailures  { get; private set; }

        public int  TotalFailures => Failures.Count;
        public bool HasFailures   => Failures.Count > 0;

        public int UniqueFailed => Failures
            .Select(f => f.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        public int UniqueDefaultFailed => Failures
            .Where(f => f.Kind is CheckKind.GetByCode
                              or CheckKind.GetByZip
                              or CheckKind.GetAllByCode)
            .Select(f => f.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        public List<IntegrityFailure> Failures { get; } = [];

        public void AddFailure(CheckKind kind, string code, string detail)
        {
            Failures.Add(new IntegrityFailure { Kind = kind, Code = code, Detail = detail });

            switch (kind)
            {
                case CheckKind.GetByCode:              GetByCodeFailures++;               break;
                case CheckKind.GetByZip:               GetByZipFailures++;                break;
                case CheckKind.GetAllByCode:           GetAllByCodeFailures++;            break;
                case CheckKind.GetAllByCodeNonDefault: GetAllByCodeNonDefaultFailures++;  break;
            }
        }
    }
}