using Dapper;
using Microsoft.Data.SqlClient;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.Normalizers;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// "ZpCodes Only" import — invoked from the dashboard (Importing Data › ZpCodes Only),
/// not the CLI. Entry point is <see cref="RunAsync(Options)"/>.
///
/// Accepts a flat file of bare postal codes (one per line or comma-separated),
/// validates each code's format via the library's per-country normalizer rules,
/// and inserts an uncurated <c>data.Reference</c> row for every new, valid code.
/// PlaceName is a "---" placeholder; TimezoneChecked / NameChecked are 0 so the
/// computed Curated column is 0 — which means 'enrich direct' picks the rows up
/// and fills in PlaceName, admin, and timezone from the APIs.
/// </summary>
public static class ImportCodesOnlyCommand
{
    /// <summary>Placeholder PlaceName for a bare code awaiting enrichment (PlaceName is NOT NULL).</summary>
    public const string PlaceNamePlaceholder = "---";

    public sealed record Options(string File, string Country, bool DryRun = false);

    public static async Task<int> RunAsync(Options opts)
    {
        Console.WriteLine($"Codes-only import: {opts.File} [{opts.Country}]");

        // --- 1. Parse codes from file ---
        var rawCodes = ParseCodesFromFile(opts.File);
        Console.WriteLine($"  Codes read: {rawCodes.Count:N0}");

        // --- 2. Normalise, validate, and deduplicate ---
        var codeRules = GetCodeRules(opts.Country);
        var valid     = new List<string>();
        var invalid   = new List<string>();

        foreach (var raw in rawCodes)
        {
            var normalized = codeRules.Normalize(raw);
            if (codeRules.Validate(normalized))
                valid.Add(normalized);
            else
                invalid.Add(raw);
        }

        valid = valid.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Console.WriteLine($"  Valid: {valid.Count:N0}  |  Invalid/skipped: {invalid.Count:N0}");

        if (invalid.Count > 0)
        {
            var preview = string.Join(", ", invalid.Take(10));
            var suffix  = invalid.Count > 10 ? $"… (+{invalid.Count - 10} more)" : "";
            Console.WriteLine($"  Invalid: {preview}{suffix}");
        }

        if (valid.Count == 0)
        {
            Console.WriteLine("  Nothing to import.");
            return 0;
        }

        // --- 3. Connect to working DB ---
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

        // --- 4. Skip codes already present in data.Reference (any PlaceName) ---
        // data.Reference has no unique (CountryId, ZpCode) constraint and the merge
        // keys on PlaceName, so a "---" row would be inserted alongside an existing
        // real row. Filter existing codes out up front. (Projection read — the
        // sanctioned inline-Dapper case; the insert below goes through db.Data.)
        await using var conn = (SqlConnection)db.GetFactory().CreateConnection();
        var existing = (await conn.QueryAsync<string>(
                CommonQueries.GetReferenceCodesForCountry,
                new { CountryId = opts.Country.ToUpperInvariant() }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newCodes = valid.Where(c => !existing.Contains(c)).ToList();
        var skipped  = valid.Count - newCodes.Count;

        if (skipped > 0)
            Console.WriteLine($"  Already in data.Reference (skipped): {skipped:N0}");

        if (newCodes.Count == 0)
        {
            Console.WriteLine("  All codes already present — nothing to import.");
            return 0;
        }

        if (opts.DryRun)
        {
            Console.WriteLine($"  [dry-run] Would insert {newCodes.Count:N0} uncurated data.Reference rows — no changes made.");
            return 0;
        }

        // --- 5. Build uncurated reference rows (PlaceName placeholder, both checks 0) ---
        var rows = newCodes.Select(code => (IDataSchema)new DataReference
        {
            CountryId       = opts.Country.ToUpperInvariant(),
            ZpCode          = code,
            PlaceName       = PlaceNamePlaceholder,
            Timezone        = "",
            IsDefault       = true,
            Lat             = "---",
            Lng             = "---",
            TimezoneChecked = false,
            NameChecked     = false,
        }).ToList();

        // --- 6. Merge reference rows via the data service ---
        var ok = await AnsiConsole.Status()
            .StartAsync(
                $"Merging {rows.Count:N0} uncurated reference rows…",
                async _ => await db.Data.MergeDataRecordsAsync(rows));

        if (!ok)
        {
            await Console.Error.WriteLineAsync("  ✗ Merge failed — see error above.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"  ✓ Added {rows.Count:N0} uncurated code(s) to data.Reference for [{opts.Country.ToUpperInvariant()}].");
        Console.WriteLine($"  Run 'enrich direct --country {opts.Country.ToUpperInvariant()}' to fill in name, admin, and timezone.");
        return 0;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static List<string> ParseCodesFromFile(string path)
    {
        var codes = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            foreach (var part in line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                    codes.Add(part.Trim());
            }
        }
        return codes;
    }

    private static ICountryCodeRules GetCodeRules(string country) =>
        country.ToUpperInvariant() switch
        {
            "CA" => new CaCountryCodeRules(),
            "MX" => new MxCountryCodeRules(),
            _    => new UsCountryCodeRules(),
        };
}
