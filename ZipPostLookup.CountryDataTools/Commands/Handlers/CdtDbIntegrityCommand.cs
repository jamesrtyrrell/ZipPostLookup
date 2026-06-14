using System.Text;
using Dapper;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.CountryRules;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools integrity cdt-db --country XX [--all] [--output path]
///
/// DB quality scan — checks the working database for data problems independently
/// of the ZipPostLookup library. Does not load any embedded CSV or ZPI image.
///
/// Checks performed:
///   1. Admin code correctness — compares stored Admin1Code against what
///      ResolveAdmin1 says it should be (US + MX only; CA has no rule).
///   2. Missing admin1 — curated codes with NULL, blank, or all-numeric Admin1Code.
///   3. Orphan AltNameOf — rows whose AltNameOf value has no matching canonical row.
///   4. Duplicate IsDefault — ZpCodes with more than one IsDefault=1 curated row.
///   5. Invalid ZpCode format — rows whose ZpCode doesn't match the country's
///      expected format (5 digits for US/MX; letter-digit-letter prefix for CA).
///   7. Curated codes with no IsDefault=1 row — GetByCode() has no authoritative primary.
///   8. AltNameOf rows marked IsDefault=1 — alt-name returned as primary result.
///   9. Curated rows with blank PlaceName — would export an empty name.
///  10. TimezoneChecked=1 but Timezone blank — claims verified but exports empty string.
///  11. Gold code regressions — gold-certified codes that no longer meet all conditions.
///  12. Open Gold Name Discrepancies — gold codes with unresolved Name discrepancies
///      that may be real aliases needing promotion.
///  13. Curated non-alphabetic place names — curated rows whose name is non-blank but
///      has no letter (e.g. "1910") — junk number artefacts that would export as garbage.
///
/// Writes a Markdown report to DataAnalysis/{cc}-db-integrity-{date}.md.
/// </summary>
public static class CdtDbIntegrityCommand
{
    // ── DTOs ──────────────────────────────────────────────────────────────────

    public sealed record CheckRow(string ZpCode, string Admin1Code);
    public sealed record OrphanRow(string ZpCode, string PlaceName, string AltNameOf);
    public sealed record DuplicateRow(string ZpCode, int DefaultCount);
    public sealed record InvalidCodeRow(long ReferenceId, string ZpCode, string PlaceName);

    public sealed record DbCheckResults(
        List<(string ZpCode, string Stored, string Expected)> AdminMismatches,
        int    MissingAdmin1Count,
        List<OrphanRow>      OrphanAltNames,
        List<DuplicateRow>   DuplicateDefaults,
        List<InvalidCodeRow> InvalidZpCodes,
        List<string>         CodesWithNoDefault,
        List<OrphanRow>      AltDefaultRows,
        List<InvalidCodeRow> BlankPlaceNames,
        List<InvalidCodeRow> NonAlphaPlaceNames,
        List<InvalidCodeRow> BlankTimezones,
        List<string>         GoldRegressions,
        List<string>         GoldNameDiscrepancies,
        bool              AdminCheckSupported);

    // ── Entry point ────────────────────────────────────────────────────────────

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        if (!TryParseArgs(args, out var country, out var all, out var output))
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
            return await CountryRunner.ForEachWithRuleAsync(async cc =>
                (await RunForCountryAsync(db, cc.ToUpperInvariant(), null)).ExitCode);

        return (await RunForCountryAsync(db, country.ToUpperInvariant(), output)).ExitCode;
    }

    // ── Per-country run ────────────────────────────────────────────────────────

    public static async Task<(int ExitCode, DbCheckResults Results, string ReportPath)> RunForCountryAsync(
        WorkDbContext db, string cc, string? output)
    {
        using var conn = db.GetFactory().CreateConnection();

        AnsiConsole.MarkupLine($"  [grey]Running CDT DB integrity checks for {cc}…[/]");

        var rules           = CountryRulesFactory.For(cc);
        var adminSupported  = rules.SupportsAdmin1Derivation;

        // ── Check 1: Admin code correctness ──────────────────────────────────
        var adminMismatches = new List<(string ZpCode, string Stored, string Expected)>();

        if (adminSupported)
        {
            Console.Write("  ▸ Admin correctness");
            var rows = (await conn.QueryAsync<CheckRow>(
                CommonQueries.GetCuratedDefaultAdminCodes,
                new { CountryId = cc })).ToList();

            foreach (var row in rows)
            {
                var expected = rules.ResolveAdmin1(row.ZpCode);
                if (expected == null) continue;

                if (!string.Equals(row.Admin1Code, expected.Value.Code,
                        StringComparison.OrdinalIgnoreCase))
                {
                    adminMismatches.Add((row.ZpCode, row.Admin1Code, expected.Value.Code));
                }
            }
            Console.WriteLine($" — {adminMismatches.Count:N0} mismatch(es)");
        }
        else
        {
            Console.WriteLine("  ▸ Admin correctness — skipped (no rule for {cc})");
        }

        // ── Check 2: Missing admin1 ───────────────────────────────────────────
        Console.Write("  ▸ Missing admin1");
        var missingAdmin1Count = await conn.ExecuteScalarAsync<int>(
            CommonQueries.GetCuratedMissingAdmin1Count, new { CountryId = cc });
        Console.WriteLine($" — {missingAdmin1Count:N0} code(s)");

        // ── Check 3: Orphan AltNameOf ─────────────────────────────────────────
        Console.Write("  ▸ Orphan AltNameOf");
        var orphans = (await conn.QueryAsync<OrphanRow>(
            CommonQueries.GetOrphanAltNamesDetailed,
            new { CountryId = cc })).ToList();
        Console.WriteLine($" — {orphans.Count:N0} row(s)");

        // ── Check 4: Duplicate IsDefault ──────────────────────────────────────
        Console.Write("  ▸ Duplicate IsDefault");
        var dupes = (await conn.QueryAsync<DuplicateRow>(
            CommonQueries.GetDuplicateIsDefaultCodes,
            new { CountryId = cc })).ToList();
        Console.WriteLine($" — {dupes.Count:N0} code(s)");

        // ── Check 5: Invalid ZpCode format ───────────────────────────────────
        Console.Write("  ▸ Invalid ZpCode format");
        var invalidCodes = (await conn.QueryAsync<InvalidCodeRow>(
            CommonQueries.GetInvalidZpCodes,
            new { CountryId = cc, ValidPattern = rules.ZpCodeLikePattern })).ToList();
        Console.WriteLine($" — {invalidCodes.Count:N0} row(s)");

        // ── Check 7: Curated codes with no IsDefault=1 row ───────────────────
        Console.Write("  ▸ Curated codes with no default row");
        var noDefaultCodes = (await conn.QueryAsync<string>(
            CommonQueries.GetCuratedCodesWithNoDefault,
            new { CountryId = cc })).ToList();
        Console.WriteLine($" — {noDefaultCodes.Count:N0} code(s)");

        // ── Check 8: AltNameOf rows marked IsDefault=1 ───────────────────────
        Console.Write("  ▸ Alt-name rows marked IsDefault");
        var altDefaultRows = (await conn.QueryAsync<OrphanRow>(
            CommonQueries.GetAltNameRowsMarkedDefault,
            new { CountryId = cc })).ToList();
        Console.WriteLine($" — {altDefaultRows.Count:N0} row(s)");

        // ── Check 9: Curated rows with blank PlaceName ───────────────────────
        Console.Write("  ▸ Curated blank place names");
        var blankPlaceNames = (await conn.QueryAsync<InvalidCodeRow>(
            CommonQueries.GetCuratedBlankPlaceNames,
            new { CountryId = cc })).ToList();
        Console.WriteLine($" — {blankPlaceNames.Count:N0} row(s)");

        // ── Check 13: Curated non-alphabetic place names ─────────────────────
        Console.Write("  ▸ Curated non-alphabetic place names");
        var nonAlphaPlaceNames = (await conn.QueryAsync<InvalidCodeRow>(
            CommonQueries.GetCuratedNonAlphaPlaceNames,
            new { CountryId = cc })).ToList();
        Console.WriteLine($" — {nonAlphaPlaceNames.Count:N0} row(s)");

        // ── Check 10: TimezoneChecked=1 but Timezone blank ───────────────────
        Console.Write("  ▸ Checked but blank timezones");
        var blankTimezones = (await conn.QueryAsync<InvalidCodeRow>(
            CommonQueries.GetCheckedBlankTimezones,
            new { CountryId = cc })).ToList();
        Console.WriteLine($" — {blankTimezones.Count:N0} row(s)");

        // ── Check 11: Gold codes that no longer meet gold conditions ──────────
        Console.Write("  ▸ Gold code regressions");
        var goldRegressions = new List<string>();
        try
        {
            goldRegressions = (await conn.QueryAsync<string>(
                CommonQueries.GetGoldCodesFailingConditions,
                new { CountryId = cc })).ToList();
        }
        catch
        {
            // data.GoldCode table may not exist yet — run migration first
        }
        Console.WriteLine($" — {goldRegressions.Count:N0} code(s)");

        // ── Check 12: Gold codes with open Name discrepancies ─────────────────
        Console.Write("  ▸ Open Gold Name discrepancies");
        var goldNameDiscrepancies = new List<string>();
        try
        {
            goldNameDiscrepancies = (await conn.QueryAsync<string>(
                CommonQueries.GetGoldNameDiscrepancies,
                new { CountryId = cc })).ToList();
        }
        catch
        {
            // data.GoldCode or codes.Discrepancies.Notes may not exist yet — run migrations first
        }
        Console.WriteLine($" — {goldNameDiscrepancies.Count:N0} code(s)");

        Console.WriteLine();

        var results = new DbCheckResults(
            adminMismatches, missingAdmin1Count, orphans, dupes, invalidCodes,
            noDefaultCodes, altDefaultRows, blankPlaceNames, nonAlphaPlaceNames, blankTimezones,
            goldRegressions, goldNameDiscrepancies, adminSupported);

        // ── Print summary table ────────────────────────────────────────────────
        PrintSummaryTable(cc, results);

        // ── Write report ──────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(output))
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "DataAnalysis");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            output = Path.Combine(dir,
                $"{cc.ToLowerInvariant()}-db-integrity-{DateTime.UtcNow:yyyyMMdd}.md");
        }

        var report = BuildMarkdownReport(cc, results);
        await File.WriteAllTextAsync(output, report);
        Console.WriteLine($"  ✓ Report written to: {output}");
        Console.WriteLine();

        var hasIssues = adminMismatches.Count > 0
            || missingAdmin1Count > 0
            || orphans.Count > 0
            || dupes.Count > 0
            || invalidCodes.Count > 0
            || noDefaultCodes.Count > 0
            || altDefaultRows.Count > 0
            || blankPlaceNames.Count > 0
            || nonAlphaPlaceNames.Count > 0
            || blankTimezones.Count > 0
            || goldRegressions.Count > 0
            || goldNameDiscrepancies.Count > 0;

        return (hasIssues ? 1 : 0, results, output);
    }

    // ── Console summary table ──────────────────────────────────────────────────

    public static void PrintSummaryTable(string cc, DbCheckResults r)
    {
        var table = new Table()
            .Title($"[bold cyan]{cc} — CDT DB Integrity[/]")
            .AddColumn(new TableColumn("[grey]Check[/]").LeftAligned())
            .AddColumn(new TableColumn("[grey]Count[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Status[/]").Centered());

        table.AddRow(
            r.AdminCheckSupported ? "Admin code correctness" : "Admin code correctness [grey](skipped)[/]",
            r.AdminCheckSupported ? $"{r.AdminMismatches.Count:N0}" : "—",
            StatusIcon(r.AdminMismatches.Count));

        table.AddRow("Missing admin1",
            $"{r.MissingAdmin1Count:N0}",
            StatusIcon(r.MissingAdmin1Count));

        table.AddRow("Orphan AltNameOf",
            $"{r.OrphanAltNames.Count:N0}",
            StatusIcon(r.OrphanAltNames.Count));

        table.AddRow("Duplicate IsDefault",
            $"{r.DuplicateDefaults.Count:N0}",
            StatusIcon(r.DuplicateDefaults.Count));

        table.AddRow("Invalid ZpCode format",
            $"{r.InvalidZpCodes.Count:N0}",
            StatusIcon(r.InvalidZpCodes.Count));

        table.AddRow("Curated codes with no default",
            $"{r.CodesWithNoDefault.Count:N0}",
            StatusIcon(r.CodesWithNoDefault.Count));

        table.AddRow("Alt-name rows marked IsDefault",
            $"{r.AltDefaultRows.Count:N0}",
            StatusIcon(r.AltDefaultRows.Count));

        table.AddRow("Curated blank place names",
            $"{r.BlankPlaceNames.Count:N0}",
            StatusIcon(r.BlankPlaceNames.Count));

        table.AddRow("Curated non-alphabetic place names",
            $"{r.NonAlphaPlaceNames.Count:N0}",
            StatusIcon(r.NonAlphaPlaceNames.Count));

        table.AddRow("Checked but blank timezones",
            $"{r.BlankTimezones.Count:N0}",
            StatusIcon(r.BlankTimezones.Count));

        table.AddRow("Gold code regressions",
            $"{r.GoldRegressions.Count:N0}",
            StatusIcon(r.GoldRegressions.Count));

        table.AddRow("Open Gold Name discrepancies",
            $"{r.GoldNameDiscrepancies.Count:N0}",
            StatusIcon(r.GoldNameDiscrepancies.Count));

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string StatusIcon(int count) =>
        count == 0 ? "[green]✓[/]" : $"[red]⚠ {count:N0}[/]";

    // ── Markdown report ────────────────────────────────────────────────────────

    private static string BuildMarkdownReport(string cc, DbCheckResults r)
    {
        var sb = new StringBuilder();
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        sb.AppendLine($"# CDT DB Integrity Report — {cc} ({date})");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Check | Count | Status |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine($"| Admin code correctness{(r.AdminCheckSupported ? "" : " (skipped)")} | {(r.AdminCheckSupported ? r.AdminMismatches.Count.ToString("N0") : "—")} | {(r.AdminMismatches.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Missing admin1 | {r.MissingAdmin1Count:N0} | {(r.MissingAdmin1Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Orphan AltNameOf | {r.OrphanAltNames.Count:N0} | {(r.OrphanAltNames.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Duplicate IsDefault | {r.DuplicateDefaults.Count:N0} | {(r.DuplicateDefaults.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Invalid ZpCode format | {r.InvalidZpCodes.Count:N0} | {(r.InvalidZpCodes.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Curated codes with no default | {r.CodesWithNoDefault.Count:N0} | {(r.CodesWithNoDefault.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Alt-name rows marked IsDefault | {r.AltDefaultRows.Count:N0} | {(r.AltDefaultRows.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Curated blank place names | {r.BlankPlaceNames.Count:N0} | {(r.BlankPlaceNames.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Curated non-alphabetic place names | {r.NonAlphaPlaceNames.Count:N0} | {(r.NonAlphaPlaceNames.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Checked but blank timezones | {r.BlankTimezones.Count:N0} | {(r.BlankTimezones.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Gold code regressions | {r.GoldRegressions.Count:N0} | {(r.GoldRegressions.Count == 0 ? "✓" : "⚠")} |");
        sb.AppendLine($"| Open Gold Name discrepancies | {r.GoldNameDiscrepancies.Count:N0} | {(r.GoldNameDiscrepancies.Count == 0 ? "✓" : "⚠")} |");

        sb.AppendLine();

        // ── Admin mismatches ──────────────────────────────────────────────────
        sb.AppendLine("## Admin Code Mismatches");
        sb.AppendLine();
        if (!r.AdminCheckSupported)
        {
            sb.AppendLine("_No rule defined for this country — check skipped._");
        }
        else if (r.AdminMismatches.Count == 0)
        {
            sb.AppendLine("_None — all admin codes match expected values._");
        }
        else
        {
            sb.AppendLine($"_{r.AdminMismatches.Count:N0} code(s) have wrong admin1 code. Run `workdb normalize-admins` to fix._");
            sb.AppendLine();
            sb.AppendLine("| ZpCode | Stored | Expected |");
            sb.AppendLine("|---|---|---|");
            foreach (var (z, s, e) in r.AdminMismatches.Take(500))
                sb.AppendLine($"| {z} | {s} | {e} |");
            if (r.AdminMismatches.Count > 500)
                sb.AppendLine($"| … | … | … |");
            sb.AppendLine($"_{Math.Min(r.AdminMismatches.Count, 500)} of {r.AdminMismatches.Count:N0} rows shown._");
        }
        sb.AppendLine();

        // ── Missing admin1 ────────────────────────────────────────────────────
        sb.AppendLine("## Missing Admin1");
        sb.AppendLine();
        sb.AppendLine(r.MissingAdmin1Count == 0
            ? "_None — all curated codes have admin1 set._"
            : $"_{r.MissingAdmin1Count:N0} curated code(s) have NULL, blank, or all-numeric admin1. Run `workdb normalize-admins` to fix._");
        sb.AppendLine();

        // ── Orphan AltNameOf ──────────────────────────────────────────────────
        sb.AppendLine("## Orphan AltNameOf");
        sb.AppendLine();
        if (r.OrphanAltNames.Count == 0)
        {
            sb.AppendLine("_None — all AltNameOf values resolve to a canonical row._");
        }
        else
        {
            sb.AppendLine($"_{r.OrphanAltNames.Count:N0} row(s) have an AltNameOf that points to a non-existent canonical name._");
            sb.AppendLine();
            sb.AppendLine("| ZpCode | PlaceName | AltNameOf |");
            sb.AppendLine("|---|---|---|");
            foreach (var o in r.OrphanAltNames.Take(200))
                sb.AppendLine($"| {o.ZpCode} | {o.PlaceName} | {o.AltNameOf} |");
            if (r.OrphanAltNames.Count > 200)
                sb.AppendLine($"| … | … | … |");
        }
        sb.AppendLine();

        // ── Duplicate IsDefault ───────────────────────────────────────────────
        sb.AppendLine("## Duplicate IsDefault Rows");
        sb.AppendLine();
        if (r.DuplicateDefaults.Count == 0)
        {
            sb.AppendLine("_None — each curated code has exactly one IsDefault=1 row._");
        }
        else
        {
            sb.AppendLine($"_{r.DuplicateDefaults.Count:N0} code(s) have multiple IsDefault=1 curated rows. The library will return the last-loaded name for these codes._");
            sb.AppendLine();
            sb.AppendLine("| ZpCode | IsDefault Count |");
            sb.AppendLine("|---|---|");
            foreach (var d in r.DuplicateDefaults.Take(200))
                sb.AppendLine($"| {d.ZpCode} | {d.DefaultCount} |");
        }
        sb.AppendLine();

        // ── Invalid ZpCode format ─────────────────────────────────────────────
        sb.AppendLine("## Invalid ZpCode Format");
        sb.AppendLine();
        if (r.InvalidZpCodes.Count == 0)
        {
            sb.AppendLine("_None — all ZpCodes match the expected format._");
        }
        else
        {
            sb.AppendLine($"_{r.InvalidZpCodes.Count:N0} row(s) have a ZpCode that does not match the expected country format. Delete or correct these rows._");
            sb.AppendLine();
            sb.AppendLine("| ReferenceId | ZpCode | PlaceName |");
            sb.AppendLine("|---|---|---|");
            foreach (var row in r.InvalidZpCodes.Take(200))
                sb.AppendLine($"| {row.ReferenceId} | {row.ZpCode} | {row.PlaceName} |");
            if (r.InvalidZpCodes.Count > 200)
                sb.AppendLine($"| … | … | … |");
        }
        sb.AppendLine();

        // ── Curated codes with no default ────────────────────────────────────
        sb.AppendLine("## Curated Codes With No Default Row");
        sb.AppendLine();
        if (r.CodesWithNoDefault.Count == 0)
        {
            sb.AppendLine("_None — all curated codes have an IsDefault=1 row._");
        }
        else
        {
            sb.AppendLine($"_{r.CodesWithNoDefault.Count:N0} curated code(s) have no IsDefault=1 row. `GetByCode()` will return a non-deterministic result for these codes. Set IsDefault=1 on the correct row in the ZpCode editor._");
            sb.AppendLine();
            sb.AppendLine("| ZpCode |");
            sb.AppendLine("|---|");
            foreach (var z in r.CodesWithNoDefault.Take(200))
                sb.AppendLine($"| {z} |");
            if (r.CodesWithNoDefault.Count > 200)
                sb.AppendLine("| … |");
        }
        sb.AppendLine();

        // ── AltNameOf rows marked IsDefault ──────────────────────────────────
        sb.AppendLine("## Alt-Name Rows Marked IsDefault");
        sb.AppendLine();
        if (r.AltDefaultRows.Count == 0)
        {
            sb.AppendLine("_None — no AltNameOf rows are marked IsDefault=1._");
        }
        else
        {
            sb.AppendLine($"_{r.AltDefaultRows.Count:N0} row(s) have AltNameOf set AND IsDefault=1. `GetByCode()` will return the alt-name instead of the canonical name. Clear IsDefault on these rows._");
            sb.AppendLine();
            sb.AppendLine("| ZpCode | PlaceName | AltNameOf |");
            sb.AppendLine("|---|---|---|");
            foreach (var o in r.AltDefaultRows.Take(200))
                sb.AppendLine($"| {o.ZpCode} | {o.PlaceName} | {o.AltNameOf} |");
            if (r.AltDefaultRows.Count > 200)
                sb.AppendLine("| … | … | … |");
        }
        sb.AppendLine();

        // ── Curated blank place names ─────────────────────────────────────────
        sb.AppendLine("## Curated Blank Place Names");
        sb.AppendLine();
        if (r.BlankPlaceNames.Count == 0)
        {
            sb.AppendLine("_None — all curated rows have a non-blank PlaceName._");
        }
        else
        {
            sb.AppendLine($"_{r.BlankPlaceNames.Count:N0} curated row(s) have a NULL, empty, or placeholder PlaceName. These will export as empty strings. Edit the place name in the ZpCode editor._");
            sb.AppendLine();
            sb.AppendLine("| ReferenceId | ZpCode | PlaceName |");
            sb.AppendLine("|---|---|---|");
            foreach (var row in r.BlankPlaceNames.Take(200))
                sb.AppendLine($"| {row.ReferenceId} | {row.ZpCode} | {row.PlaceName} |");
            if (r.BlankPlaceNames.Count > 200)
                sb.AppendLine("| … | … | … |");
        }
        sb.AppendLine();

        // ── Curated non-alphabetic place names ────────────────────────────────
        sb.AppendLine("## Curated Non-Alphabetic Place Names");
        sb.AppendLine();
        if (r.NonAlphaPlaceNames.Count == 0)
        {
            sb.AppendLine("_None — all curated place names contain at least one letter._");
        }
        else
        {
            sb.AppendLine($"_{r.NonAlphaPlaceNames.Count:N0} curated row(s) have a non-blank PlaceName with no alphabetic character (e.g. \"1910\", \"20 30\") — junk number artefacts that would export as garbage names. Delete the offending non-default rows or supply a real place name._");
            sb.AppendLine();
            sb.AppendLine("| ReferenceId | ZpCode | PlaceName |");
            sb.AppendLine("|---|---|---|");
            foreach (var row in r.NonAlphaPlaceNames.Take(200))
                sb.AppendLine($"| {row.ReferenceId} | {row.ZpCode} | {row.PlaceName} |");
            if (r.NonAlphaPlaceNames.Count > 200)
                sb.AppendLine("| … | … | … |");
        }
        sb.AppendLine();

        // ── Checked but blank timezones ───────────────────────────────────────
        sb.AppendLine("## Checked But Blank Timezones");
        sb.AppendLine();
        if (r.BlankTimezones.Count == 0)
        {
            sb.AppendLine("_None — all TimezoneChecked rows have a non-blank Timezone._");
        }
        else
        {
            sb.AppendLine($"_{r.BlankTimezones.Count:N0} row(s) have TimezoneChecked=1 but a NULL, empty, or placeholder Timezone. These will export an empty timezone string. Edit the timezone in the ZpCode editor._");
            sb.AppendLine();
            sb.AppendLine("| ReferenceId | ZpCode | PlaceName |");
            sb.AppendLine("|---|---|---|");
            foreach (var row in r.BlankTimezones.Take(200))
                sb.AppendLine($"| {row.ReferenceId} | {row.ZpCode} | {row.PlaceName} |");
            if (r.BlankTimezones.Count > 200)
                sb.AppendLine("| … | … | … |");
        }
        sb.AppendLine();

        // ── Gold code regressions ─────────────────────────────────────────────
        sb.AppendLine("## Gold Code Regressions");
        sb.AppendLine();
        if (r.GoldRegressions.Count == 0)
        {
            sb.AppendLine("_None — all gold-certified codes still meet gold conditions._");
        }
        else
        {
            sb.AppendLine($"_{r.GoldRegressions.Count:N0} gold-certified code(s) no longer meet all gold conditions (curated default row, admin1, timezone, lat/lng). Run `db integrity cdt-db` to review; revoke or re-curate these codes._");
            sb.AppendLine();
            sb.AppendLine("| ZpCode |");
            sb.AppendLine("|---|");
            foreach (var z in r.GoldRegressions.Take(200))
                sb.AppendLine($"| {z} |");
            if (r.GoldRegressions.Count > 200)
                sb.AppendLine("| … |");
        }
        sb.AppendLine();

        // ── Open Gold Name discrepancies ──────────────────────────────────────
        sb.AppendLine("## Open Gold Name Discrepancies");
        sb.AppendLine();
        if (r.GoldNameDiscrepancies.Count == 0)
        {
            sb.AppendLine("_None — no gold codes have unresolved Name discrepancies._");
        }
        else
        {
            sb.AppendLine($"_{r.GoldNameDiscrepancies.Count:N0} gold-certified code(s) have unresolved Name discrepancies. The incoming name may be a real alias worth promoting. Review in `codes.Discrepancies` (FieldName='Name', ResolvedAt IS NULL) and add as AltNameOf if confirmed._");
            sb.AppendLine();
            sb.AppendLine("| ZpCode |");
            sb.AppendLine("|---|");
            foreach (var z in r.GoldNameDiscrepancies.Take(200))
                sb.AppendLine($"| {z} |");
            if (r.GoldNameDiscrepancies.Count > 200)
                sb.AppendLine("| … |");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TryParseArgs(
        string[] args, out string country, out bool all, out string? output)
    {
        country = (args.OptionValue("--country") ?? "US").ToUpperInvariant();
        all     = args.HasFlag("--all");
        output  = args.OptionValue("--output");

        return all || !string.IsNullOrWhiteSpace(country);
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools integrity cdt-db [--country XX] [--all] [--output path]

              Scans the working DB for data-quality problems independent of the library.

              Checks:
                · Admin code correctness       (US + MX — compares stored vs expected from rules)
                · Missing admin1               (curated codes with NULL/blank/numeric admin)
                · Orphan AltNameOf             (AltNameOf pointing to non-existent canonical)
                · Duplicate IsDefault          (codes with > 1 IsDefault=1 curated row)
                · Invalid ZpCode format        (rows with malformed ZpCode for the country)
                · Curated codes with no default (no IsDefault=1 row — lookup is non-deterministic)
                · Alt-name rows marked default (AltNameOf + IsDefault=1 — wrong primary result)
                · Curated blank place names    (NULL/empty PlaceName on curated rows)
                · Curated non-alpha names      (curated PlaceName with no letter — e.g. "1910")
                · Checked but blank timezones  (TimezoneChecked=1 but Timezone is blank)
                · Gold code regressions        (gold-certified codes that lost qualifying conditions)
                · Open Gold Name discrepancies (gold codes with unresolved Name discrepancies — may be aliases)

              --country XX   Country code (US, CA, MX).  Default: US
              --all          Run for all three countries in sequence.
              --output path  Write report to this path instead of DataAnalysis/.
            """);
}
