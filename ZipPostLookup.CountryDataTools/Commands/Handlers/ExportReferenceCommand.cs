using System.IO.Compression;
using Dapper;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Utilities;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Validation.Export;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Export;
using ZipPostLookup.CountryDataTools.Export.ZpImage;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Models.Json;
using ZipPostLookup.Normalizers;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools export --country CA [--target ref|main|zpi] [--output path] [--curated-only] [--uncompressed]
///
/// Three export targets:
///
///   --target ref  (source of truth)
///     Exports [data].[reference] as a full ReferenceDataCSV including TimezoneChecked
///     and NameChecked columns. Default output:
///       ZipPostLookup.CountryDataTools/Data/{cc}/{cc}.csv
///     The directory is created automatically. No pipeline transformation is applied.
///     Re-import this file to restore the working database from scratch.
///
///   --target main  (default — ZipPostLookup project)
///     Exports an optimised ZipPostLookupDataCSV for consumption by BuiltInDataSource.
///     For CA, US, and MX the full pipeline is applied:
///       1. Lat/lng columns stripped when coverage is below 20% threshold
///       2. Homogeneous prefix groups collapsed to range rows (e.g. "T0A0**:T0A9**")
///       3. Timezone and admin1 strings replaced with integer indices
///       4. A #meta: header line written so BuiltInDataSource can decode the file
///     Default output: {cc}.csv in the current directory.
///
///   --target zpi  (frozen binary image)
///     Exports the same optimised data as --target main, but as a ZP frozen image
///     (a minimal-perfect-hash binary blob) instead of CSV. Default output:
///       ZipPostLookup/Data/{cc}/{cc}.zpi.br  (Brotli; --uncompressed writes {cc}.zpi)
///     Built for a zero-parse, zero-allocation load path. See Export/ZPimage.
///
/// Shared behaviour:
///   · Code codes are passed through the country normalizer (restores leading zeros).
///   · Exactly one IsDefault=true is enforced per code group.
///   · --curated-only applies to all targets.
/// </summary>
public static class ExportReferenceCommand
{
    /// <summary>Countries that use the full optimised pipeline for --target main.</summary>
    private static readonly HashSet<string> _pipelineCountries =
        new(StringComparer.OrdinalIgnoreCase) { "CA", "US", "MX" };

    private static readonly System.Text.Json.JsonSerializerOptions _metaJsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // Named record so Dapper maps by property name rather than by ordinal position.
    private sealed record AdminDivisionCount(string AdminCode, int NameCount, int ZipCount);

    // =========================================================================
    // Entry point
    // =========================================================================

    public sealed record Options(
        string Country      = "",
        ExportTarget Target = ExportTarget.Main,
        string Output       = "",
        bool CuratedOnly    = false,
        bool Uncompressed   = false,
        bool All            = false,
        string? FromCsv     = null);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        var country      = args.OptionValue("--country", rejectFlagValue: true) ?? "";
        var output       = args.OptionValue("--output") ?? "";
        var curatedOnly  = args.HasFlag("--curated-only");
        var uncompressed = args.HasFlag("--uncompressed");
        var all          = args.HasFlag("--all");
        var hasFromCsv   = args.HasFlag("--from-csv");
        var fromCsv      = hasFromCsv ? (args.OptionValue("--from-csv") ?? "") : null;
        var target       = (args.OptionValue("--target") ?? "").ToLowerInvariant() switch
        {
            "ref" or "reference" => ExportTarget.Ref,
            "zpi" or "image"     => ExportTarget.Zpi,
            _                    => ExportTarget.Main,
        };

        if (!all && string.IsNullOrWhiteSpace(country))
        {
            PrintUsage();
            return 2;
        }

        return await RunAsync(new Options(country, target, output, curatedOnly, uncompressed, all, fromCsv));
    }

    public static async Task<int> RunAsync(Options opts)
    {
        if (opts.All)
        {
            return await RunAllAsync(opts.CuratedOnly, opts.Target);
        }

        var country  = opts.Country;
        var cc      = country.ToLowerInvariant();
        var ccUpper = country.ToUpperInvariant();

        // ── Offline zpi rebuild: source the optimised CSV directly, no working database. ──────
        // Use when the image format changes but the data has not. The CSV is the exact data the
        // runtime registry loads, so the rebuilt image is parity-guaranteed against it.
        if (opts.FromCsv != null)
        {
            if (opts.Target != ExportTarget.Zpi)
            {
                await Console.Error.WriteLineAsync(
                    "  ✗ --from-csv only applies to --target zpi (it rebuilds the frozen image " +
                    "from an already-optimised CSV).");
                return 2;
            }

            return await RunZpiFromCsvAsync(cc, ccUpper, opts.FromCsv, opts.Output, opts.Uncompressed);
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

        var normalizer = GetNormalizer(ccUpper);

        return opts.Target switch
        {
            ExportTarget.Ref => await RunRefExportAsync(db, cc, ccUpper, opts.Output, opts.CuratedOnly, normalizer),
            ExportTarget.Zpi => await RunZpiExportAsync(db, cc, ccUpper, opts.Output, opts.CuratedOnly, opts.Uncompressed, normalizer),
            _                => await RunMainExportAsync(db, cc, ccUpper, opts.Output, opts.CuratedOnly, normalizer),
        };
    }

    // =========================================================================
    // --all  — export for every pipeline country (target-aware)
    // =========================================================================

    // Typed entry points used by SnapshotCommand to avoid raw string[] coupling.
    // The WorkDbContext overloads let callers share a single DB connection across both steps.
    internal static Task<int> RunAllRefExportAsync(bool curatedOnly)
        => RunAllAsync(curatedOnly, ExportTarget.Ref);

    internal static Task<int> RunAllMainZpiExportAsync(bool curatedOnly)
        => RunAllAsync(curatedOnly, ExportTarget.Main);

    internal static Task<int> RunAllRefExportAsync(WorkDbContext db, bool curatedOnly)
        => RunAllCoreAsync(db, curatedOnly, ExportTarget.Ref);

    internal static Task<int> RunAllMainZpiExportAsync(WorkDbContext db, bool curatedOnly)
        => RunAllCoreAsync(db, curatedOnly, ExportTarget.Main);

    private static async Task<int> RunAllAsync(bool curatedOnly, ExportTarget target)
    {
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

        return await RunAllCoreAsync(db, curatedOnly, target);
    }

    private static async Task<int> RunAllCoreAsync(WorkDbContext db, bool curatedOnly, ExportTarget target)
    {
        var countries      = new[] { "US", "CA", "MX" };
        var exitCode       = 0;
        var countriesPath  = Path.Combine(db.RepoRoot, "ZipPostLookup.CountryDataTools", "Data", "countries.json");

        // Load countries.json once; per-country metadata calls patch this shared list in-memory.
        // A single write at the end replaces the per-country read+write pattern.
        List<CountriesJson>? sharedCountries = null;
        if (File.Exists(countriesPath))
        {
            sharedCountries = System.Text.Json.JsonSerializer.Deserialize<List<CountriesJson>>(
                await File.ReadAllTextAsync(countriesPath), _metaJsonOpts);
        }

        if (target == ExportTarget.Ref)
        {
            foreach (var ccUpper in countries)
            {
                var cc         = ccUpper.ToLowerInvariant();
                var normalizer = GetNormalizer(ccUpper);

                Console.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]{ccUpper}[/] — --target ref").LeftJustified());

                var refResult = await RunRefExportAsync(db, cc, ccUpper, "", curatedOnly, normalizer, sharedCountries);
                if (refResult != 0)
                {
                    exitCode = refResult;
                }
            }

            await FlushSharedCountriesJsonAsync(sharedCountries, countriesPath);

            Console.WriteLine();
            Console.WriteLine(exitCode == 0
                ? "✓ All ref exports complete (US + CA + MX)."
                : "✗ One or more ref exports failed — check output above.");
            return exitCode;
        }

        // Default: main + zpi
        foreach (var ccUpper in countries)
        {
            var cc         = ccUpper.ToLowerInvariant();
            var normalizer = GetNormalizer(ccUpper);

            Console.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]{ccUpper}[/] — --target main").LeftJustified());

            var mainResult = await RunMainExportAsync(db, cc, ccUpper, "", curatedOnly, normalizer, sharedCountries);
            if (mainResult != 0) { exitCode = mainResult; continue; }

            Console.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]{ccUpper}[/] — --target zpi").LeftJustified());

            var zpiResult = await RunZpiExportAsync(db, cc, ccUpper, "", curatedOnly, false, normalizer);
            if (zpiResult != 0)
            {
                exitCode = zpiResult;
            }
        }

        await FlushSharedCountriesJsonAsync(sharedCountries, countriesPath);

        Console.WriteLine();
        Console.WriteLine(exitCode == 0
            ? "✓ All exports complete (US + CA + MX, main + zpi)."
            : "✗ One or more exports failed — check output above.");

        return exitCode;
    }

    private static async Task FlushSharedCountriesJsonAsync(List<CountriesJson>? sharedCountries, string path)
    {
        if (sharedCountries == null) return;
        await File.WriteAllTextAsync(path,
            System.Text.Json.JsonSerializer.Serialize(sharedCountries, _metaJsonOpts),
            System.Text.Encoding.UTF8);
        await FileHash.WriteSidecarAsync(path);
        Console.WriteLine("  ✓ Updated countries.json (US + CA + MX)");
    }

    // =========================================================================
    // --target ref  — full ReferenceDataCSV export
    // =========================================================================

    private static async Task<int> RunRefExportAsync(
        WorkDbContext      db,
        string             cc,
        string             ccUpper,
        string             output,
        bool               curatedOnly,
        ICountryCodeRules? normalizer,
        List<CountriesJson>? sharedCountries = null)
    {
        // Default path: ZipPostLookup.CountryDataTools/Data/{cc}/{cc}.csv.tar.gz
        // relative to the repo root (where workdb.json lives). The reference CSVs are large
        // (CA ~120MB raw, over GitHub's 100MB limit) so the committed source-of-truth artifact is
        // a gzip-compressed tar; 'ingest ref' expands it in-memory (see TarGzArchive / EmbeddedCsvLoader).
        // Pass --output …{cc}.csv to write a plain (uncompressed) CSV instead.
        if (string.IsNullOrWhiteSpace(output))
        {
            var dir = Path.Combine(
                Directory.GetCurrentDirectory(), "ZipPostLookup.CountryDataTools", "Data", cc);
            Directory.CreateDirectory(dir);
            output = Path.Combine(dir, $"{cc}{TarGzArchive.Suffix}");
        }

        ExportDisplay.PrintRefHeader(ccUpper, curatedOnly, output);

        using var conn = db.GetFactory().CreateConnection();

        await RunPreChecksAsync(conn, ccUpper, db.RepoRoot);

        var sql = curatedOnly ? CommonQueries.ExportReferenceDataCuratedOnlyWithCuration : CommonQueries.ExportReferenceDataWithCuration;
        var rows = (await conn.QueryAsync<DataReference>(
            sql, new { CountryId = ccUpper })).ToList();

        Console.WriteLine($"  Rows to export: {rows.Count:N0}");

        if (rows.Count == 0)
        {
            await Console.Error.WriteLineAsync("  ✗ No rows found.");
            return 1;
        }

        NormalizeZips(rows, normalizer);
        int coordsBlanked = EnforceCoordinatePairs(rows);
        if (coordsBlanked > 0)
            Console.WriteLine($"  ⚠ {coordsBlanked:N0} row(s) had an incomplete lat/lng pair — both coordinates blanked.");
        int defaultsFixed = EnforceOneDefaultPerZip(rows);
        if (defaultsFixed > 0)
        {
            Console.WriteLine($"  ⚠ {defaultsFixed:N0} code(s) had multiple IsDefault=1 rows — " +
                              $"demoted extras to false.");
        }

        // Build the full CSV (UTF-8 with BOM) in memory, then emit either a gzip-compressed tar
        // (default — when the path ends with .tar.gz) or a plain CSV (when --output …{cc}.csv).
        var buffer = new MemoryStream();
        await using (var writer = new StreamWriter(
            buffer,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 1 << 16,
            leaveOpen: true))
        {
            await writer.WriteLineAsync(
                "ZpCode,PlaceName,Timezone,IsDefault,Lat,Lng,Admin1,Admin1Code,TimezoneChecked,NameChecked,AltNameOf");

            foreach (var row in rows)
            {
                await writer.WriteLineAsync(string.Join(',',
                    AlwaysQuote(row.ZpCode),
                    AlwaysQuote(row.PlaceName       ?? ""),
                    AlwaysQuote(row.Timezone        ?? "---"),
                    row.IsDefault ? "true" : "false",
                    AlwaysQuote(row.Lat             ?? "---"),
                    AlwaysQuote(row.Lng             ?? "---"),
                    AlwaysQuote(row.Admin1          ?? "---"),
                    AlwaysQuote(row.Admin1Code      ?? "---"),
                    row.TimezoneChecked ? "true" : "false",
                    row.NameChecked     ? "true" : "false",
                    AlwaysQuote(row.AltNameOf       ?? "")));
            }
        }

        var csvBytes = buffer.ToArray();

        if (output.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            // Entry name is the logical CSV file (e.g. "ca.csv") so `tar -xvzf` and the in-process
            // reader (TarGzArchive / EmbeddedCsvLoader) both recover {cc}.csv.
            await TarGzArchive.WriteSingleFileAsync(output, $"{cc}.csv", csvBytes);
            await FileHash.WriteSidecarAsync(output);
            Console.WriteLine(
                $"  ✓ Written {rows.Count:N0} rows ({csvBytes.Length:N0} B raw → " +
                $"{new FileInfo(output).Length:N0} B gzip) to {output}");
        }
        else
        {
            await File.WriteAllBytesAsync(output, csvBytes);
            await FileHash.WriteSidecarAsync(output);
            Console.WriteLine($"  ✓ Written {rows.Count:N0} rows to {output}");
        }

        await UpdateCountryMetadataAsync(conn, cc, ccUpper, db.RepoRoot, sharedCountries);
        return 0;
    }

    // =========================================================================
    // Post-ref-export: sync {cc}_info.json + countries.json from the DB
    // =========================================================================

    private static async Task UpdateCountryMetadataAsync(
        System.Data.IDbConnection conn, string cc, string ccUpper, string repoRoot,
        List<CountriesJson>? sharedCountries = null)
    {
        // --- DB queries ---
        var countryInfo = await conn.QueryFirstOrDefaultAsync<DataCountryInfo>(
            CommonQueries.GetCountryInfoById, new { CountryId = ccUpper });

        var distinctCodes = await conn.ExecuteScalarAsync<int>(
            CommonQueries.GetDistinctCodeCount, new { CountryId = ccUpper });

        // Keep the stored data.CountryInfo.CodeCount in sync with the curated distinct-code
        // count (it is otherwise only ever reset to 0 — see AI-DB-OVERVIEW L-3).
        await conn.ExecuteAsync(
            CommonQueries.SetCountryCodeCount, new { CountryId = ccUpper });

        var divisionRows = (await conn.QueryAsync<AdminDivisionCount>(
            CommonQueries.GetAdminDivisionCounts,
            new { CountryId = ccUpper })).ToList();

        var divByCode = divisionRows
            .Where(d => d.AdminCode != "---")
            .ToDictionary(d => d.AdminCode, d => (d.ZipCount, d.NameCount),
                StringComparer.OrdinalIgnoreCase);

        var timezoneCount = await conn.ExecuteScalarAsync<int>(
            CommonQueries.GetDistinctTimezoneCount, new { CountryId = ccUpper });

        var divisionCount = divByCode.Count;

        var curated      = countryInfo?.DataCurated ?? false;
        var curationStr  = (countryInfo?.CurationStatus ?? CurationStatus.NoData).ToString();
        var today        = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

        // --- Update {cc}_info.json ---
        var infoDir  = Path.Combine(repoRoot, "ZipPostLookup.CountryDataTools", "Data", cc);
        var infoPath = Path.Combine(infoDir, $"{cc}_info.json");

        if (File.Exists(infoPath))
        {
            var infoJson = await File.ReadAllTextAsync(infoPath);
            var info     = System.Text.Json.JsonSerializer.Deserialize<CountryInfoJson>(infoJson, _metaJsonOpts);

            if (info != null)
            {
                info.CodeCount       = distinctCodes;
                info.Curated         = curated;
                info.CurationStatus  = curationStr;
                info.LastUpdated     = today;

                foreach (var div in info.Divisions)
                {
                    if (div.Code != null && divByCode.TryGetValue(div.Code, out var counts))
                    {
                        div.ZipCount  = counts.ZipCount;
                        div.NameCount = counts.NameCount;
                    }
                }

                await File.WriteAllTextAsync(infoPath,
                    System.Text.Json.JsonSerializer.Serialize(info, _metaJsonOpts),
                    System.Text.Encoding.UTF8);
                await FileHash.WriteSidecarAsync(infoPath);

                Console.WriteLine($"  ✓ Updated {cc}_info.json " +
                    $"(CodeCount={distinctCodes:N0}, CurationStatus={curationStr}, LastUpdated={today})");
            }
        }
        else
        {
            Console.WriteLine($"  ⚠ {cc}_info.json not found at {infoPath} — skipping metadata update.");
        }

        // --- Update README.md ---
        await ReadmeUpdater.UpdateCountryAsync(repoRoot, cc, distinctCodes, timezoneCount, divisionCount);
        Console.WriteLine($"  ✓ Updated README.md tags for {ccUpper} " +
            $"(codes={distinctCodes:N0}, tz={timezoneCount}, divs={divisionCount})");

        // --- Update countries.json ---
        // When sharedCountries is provided (--all path), patch the in-memory list only —
        // the caller writes the file once after all countries are processed.
        // When null (single-country path), read → patch → write as before.
        if (sharedCountries != null)
        {
            var entry = sharedCountries.FirstOrDefault(c =>
                string.Equals(c.CountryId, ccUpper, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                entry.DataCurated    = curated;
                entry.CurationStatus = countryInfo?.CurationStatus ?? CurationStatus.NoData;
            }
        }
        else
        {
            var countriesPath = Path.Combine(repoRoot, "ZipPostLookup.CountryDataTools", "Data", "countries.json");
            if (File.Exists(countriesPath))
            {
                var countriesJson = await File.ReadAllTextAsync(countriesPath);
                var countries = System.Text.Json.JsonSerializer
                    .Deserialize<List<CountriesJson>>(countriesJson, _metaJsonOpts);

                var entry = countries?.FirstOrDefault(c =>
                    string.Equals(c.CountryId, ccUpper, StringComparison.OrdinalIgnoreCase));

                if (entry != null)
                {
                    entry.DataCurated    = curated;
                    entry.CurationStatus = countryInfo?.CurationStatus ?? CurationStatus.NoData;

                    await File.WriteAllTextAsync(countriesPath,
                        System.Text.Json.JsonSerializer.Serialize(countries, _metaJsonOpts),
                        System.Text.Encoding.UTF8);
                    await FileHash.WriteSidecarAsync(countriesPath);

                    Console.WriteLine($"  ✓ Updated countries.json " +
                        $"({ccUpper}: DataCurated={curated}, CurationStatus={curationStr})");
                }
            }
            else
            {
                Console.WriteLine($"  ⚠ countries.json not found at {countriesPath} — skipping.");
            }
        }
    }

    // =========================================================================
    // --target main  — optimised ZipPostLookup export (existing behaviour)
    // =========================================================================

    private static async Task<int> RunMainExportAsync(
        WorkDbContext      db,
        string             cc,
        string             ccUpper,
        string             output,
        bool               curatedOnly,
        ICountryCodeRules? normalizer,
        List<CountriesJson>? sharedCountries = null)
    {
        // Default path: ZipPostLookup/Data/{cc}/{cc}.csv
        // relative to the repo root (where workdb.json lives).
        if (string.IsNullOrWhiteSpace(output))
        {
            var dir = Path.Combine(
                Directory.GetCurrentDirectory(), "ZipPostLookup", "Data", cc);
            Directory.CreateDirectory(dir);
            output = Path.Combine(dir, $"{cc}.csv");
        }

        ExportDisplay.PrintMainHeader(ccUpper, curatedOnly, output, _pipelineCountries.Contains(cc));

        using var conn = db.GetFactory().CreateConnection();

        var sql = curatedOnly
            ? CommonQueries.ExportReferenceDataCuratedOnly
            : CommonQueries.ExportReferenceData;

        var rows = (await conn.QueryAsync<DataReference>(
            sql, new { CountryId = ccUpper })).ToList();

        Console.WriteLine($"  Rows to export: {rows.Count:N0}");

        if (rows.Count == 0)
        {
            await Console.Error.WriteLineAsync("  ✗ No rows found.");
            return 1;
        }

        NormalizeZips(rows, normalizer);
        int coordsBlanked = EnforceCoordinatePairs(rows);
        if (coordsBlanked > 0)
            Console.WriteLine($"  ⚠ {coordsBlanked:N0} row(s) had an incomplete lat/lng pair — both coordinates blanked.");
        int defaultsFixed = EnforceOneDefaultPerZip(rows);
        if (defaultsFixed > 0)
        {
            Console.WriteLine($"  ⚠ {defaultsFixed:N0} code(s) had multiple IsDefault=1 rows — " +
                              $"demoted extras to false (kept last-alphabetical name as default).");
        }

        // ── Optimised pipeline (US, CA, MX) ──────────────────────────────────
        if (_pipelineCountries.Contains(cc))
        {
            var exportRows = rows
                .Select(r => new ExportRow
                {
                    ZpCode     = r.ZpCode,
                    PlaceName  = r.PlaceName  ?? "",
                    Timezone   = r.Timezone   ?? "",
                    IsDefault  = r.IsDefault,
                    Lat        = r.Lat        ?? "---",
                    Lng        = r.Lng        ?? "---",
                    Admin1     = r.Admin1     ?? "---",
                    Admin1Code = r.Admin1Code ?? "---",
                })
                .ToList();

            // Query admin level names from the DB and embed them in the CSV #meta: line
            // so the library can read them without a separate config file.
            var levelNames = (await conn.QueryAsync<string>(
                CommonQueries.GetAdminLevelNames,
                new { CountryId = ccUpper })).ToArray();

            var meta = await ExportPipeline.RunAsync(cc, exportRows, output, levelNames);

            Console.WriteLine();
            Console.WriteLine("  ✓ Optimised export complete.");
            Console.WriteLine($"    Lat/lng  : {(meta.IncludeCoords ? "included" : "stripped")}");
            Console.WriteLine($"    Timezones: {(meta.TimezoneIndex != null ? $"{meta.TimezoneIndex.Length} indexed" : "verbatim")}");
            Console.WriteLine($"    Admin1   : {(meta.AdminIndex    != null ? $"{meta.AdminIndex.Length} indexed"    : "verbatim")}");
            Console.WriteLine($"    Levels   : {string.Join(", ", levelNames)}");
            Console.WriteLine($"  ✓ Written to {output}");
            await FileHash.WriteSidecarAsync(output);
            await WriteBrotliCompanionAsync(output);
            await TestableCodesGenerator.UpdateAsync(db.RepoRoot, ccUpper, output);
            await BenchCodesGenerator.UpdateAsync(db.RepoRoot, ccUpper, output);
            await ReadmeUpdater.UpdateEntriesAsync(db.RepoRoot, cc, meta.RowCount);
            await UpdateCountryMetadataAsync(conn, cc, ccUpper, db.RepoRoot, sharedCountries);
            return 0;
        }

        // ── Standard export (other countries) ────────────────────────────────
        await using var writer = new StreamWriter(
            output,
            append:   false,
            encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await writer.WriteLineAsync(
            "ZpCode,PlaceName,Timezone,IsDefault,Lat,Lng,Admin1,Admin1Code");

        foreach (var row in rows)
        {
            await writer.WriteLineAsync(string.Join(',',
                AlwaysQuote(row.ZpCode),
                AlwaysQuote(row.PlaceName  ?? ""),
                AlwaysQuote(row.Timezone   ?? ""),
                row.IsDefault ? "true" : "false",
                AlwaysQuote(row.Lat        ?? "---"),
                AlwaysQuote(row.Lng        ?? "---"),
                AlwaysQuote(row.Admin1     ?? "---"),
                AlwaysQuote(row.Admin1Code ?? "---")));
        }

        Console.WriteLine($"  ✓ Written {rows.Count:N0} rows to {output}");
        await FileHash.WriteSidecarAsync(output);
        await UpdateCountryMetadataAsync(conn, cc, ccUpper, db.RepoRoot);
        return 0;
    }

    // =========================================================================
    // --target zpi  — optimised ZP frozen image (binary)
    // =========================================================================

    private static async Task<int> RunZpiExportAsync(
        WorkDbContext      db,
        string             cc,
        string             ccUpper,
        string             output,
        bool               curatedOnly,
        bool               uncompressed,
        ICountryCodeRules? normalizer)
    {
        // Default path: ZipPostLookup/Data/{cc}/{cc}.zpi  (the writer appends .br when compressing)
        if (string.IsNullOrWhiteSpace(output))
        {
            var dir = Path.Combine(
                Directory.GetCurrentDirectory(), "ZipPostLookup", "Data", cc);
            Directory.CreateDirectory(dir);
            output = Path.Combine(dir, $"{cc}.zpi");
        }

        ExportDisplay.PrintZpiHeader(ccUpper, curatedOnly, uncompressed, output);

        using var conn = db.GetFactory().CreateConnection();

        var sql = curatedOnly
            ? CommonQueries.ExportReferenceDataCuratedOnly
            : CommonQueries.ExportReferenceData;

        var rows = (await conn.QueryAsync<DataReference>(
            sql, new { CountryId = ccUpper })).ToList();

        Console.WriteLine($"  Rows to export: {rows.Count:N0}");

        if (rows.Count == 0)
        {
            await Console.Error.WriteLineAsync("  ✗ No rows found.");
            return 1;
        }

        NormalizeZips(rows, normalizer);
        int coordsBlanked = EnforceCoordinatePairs(rows);
        if (coordsBlanked > 0)
            Console.WriteLine($"  ⚠ {coordsBlanked:N0} row(s) had an incomplete lat/lng pair — both coordinates blanked.");
        int defaultsFixed = EnforceOneDefaultPerZip(rows);
        if (defaultsFixed > 0)
        {
            Console.WriteLine($"  ⚠ {defaultsFixed:N0} code(s) had multiple IsDefault=1 rows — " +
                              $"demoted extras to false (kept last-alphabetical name as default).");
        }

        var exportRows = rows
            .Select(r => new ExportRow
            {
                ZpCode     = r.ZpCode,
                PlaceName  = r.PlaceName  ?? "",
                Timezone   = r.Timezone   ?? "",
                IsDefault  = r.IsDefault,
                Lat        = r.Lat        ?? "---",
                Lng        = r.Lng        ?? "---",
                Admin1     = r.Admin1     ?? "---",
                Admin1Code = r.Admin1Code ?? "---",
            })
            .ToList();

        // For pipeline countries, apply the same optimisation stages as --target main so the
        // image mirrors the embedded CSV the registry would otherwise load (range rows go to
        // the image's range table; tz/admin1 index tables are reused for byte-consistency).
        List<ExportRow> finalRows;
        ExportMeta?     meta;

        if (_pipelineCountries.Contains(cc))
        {
            (finalRows, meta) = ExportPipeline.Transform(cc, exportRows);
        }
        else
        {
            finalRows = exportRows;
            meta      = null;
        }

        var zpiResult = await ZpImageWriter.WriteAsync(ccUpper, finalRows, output, meta, compress: !uncompressed);
        await FileHash.WriteSidecarAsync(zpiResult.OutputPath);

        Console.WriteLine();
        Console.WriteLine("  ✓ Frozen image export complete.");
        return 0;
    }

    // =========================================================================
    // --target zpi --from-csv  — offline frozen-image rebuild (no database)
    // =========================================================================

    /// <summary>
    /// Rebuilds the ZP frozen image from an already-optimised embedded CSV instead of the working
    /// database. The optimisation pipeline is <b>not</b> re-run — the CSV is consumed verbatim
    /// (range rows preserved, tz/admin index tables decoded from its <c>#meta:</c> header), so the
    /// image mirrors exactly what <c>ZipPostRegistry</c> loads. Intended for regenerating the image
    /// after an on-disk format change when the underlying data is unchanged and Docker/SQL is not
    /// available.
    /// </summary>
    private static async Task<int> RunZpiFromCsvAsync(
        string cc,
        string ccUpper,
        string fromCsv,
        string output,
        bool   uncompressed)
    {
        // Default source: the committed optimised CSV for this country.
        var source = string.IsNullOrWhiteSpace(fromCsv)
            ? Path.Combine(Directory.GetCurrentDirectory(), "ZipPostLookup", "Data", cc, $"{cc}.csv")
            : fromCsv;

        if (!File.Exists(source))
        {
            await Console.Error.WriteLineAsync($"  ✗ Source CSV not found: {source}");
            return 1;
        }

        // Default output: ZipPostLookup/Data/{cc}/{cc}.zpi  (the writer appends .br when compressing)
        if (string.IsNullOrWhiteSpace(output))
        {
            var dir = Path.Combine(
                Directory.GetCurrentDirectory(), "ZipPostLookup", "Data", cc);
            Directory.CreateDirectory(dir);
            output = Path.Combine(dir, $"{cc}.zpi");
        }

        Console.WriteLine($"Rebuilding frozen image from {source}  [zpi / from-csv, no DB]");
        Console.WriteLine($"  Country      : {ccUpper}");
        Console.WriteLine($"  Compression  : {(uncompressed ? "none (.zpi)" : "Brotli (.zpi.br)")}");

        var (rows, meta) = OptimisedCsvSource.Read(source);

        Console.WriteLine($"  Rows read    : {rows.Count:N0}");
        if (rows.Count == 0)
        {
            await Console.Error.WriteLineAsync("  ✗ No data rows found in the CSV.");
            return 1;
        }

        var fromCsvResult = await ZpImageWriter.WriteAsync(ccUpper, rows, output, meta, compress: !uncompressed);
        await FileHash.WriteSidecarAsync(fromCsvResult.OutputPath);

        Console.WriteLine();
        Console.WriteLine("  ✓ Frozen image rebuilt from CSV.");
        return 0;
    }

    // =========================================================================
    // Shared normalisation — applied to both export targets
    // =========================================================================

    private static ICountryCodeRules? GetNormalizer(string ccUpper) =>
        ccUpper switch
        {
            "US" => new UsCountryCodeRules(),
            "CA" => new CaCountryCodeRules(),
            "MX" => new MxCountryCodeRules(),
            _    => null
        };

    /// <summary>
    /// Enforces the lat/lng pairing rule before export: any row whose coordinates do not
    /// form a complete, parseable pair has BOTH coordinates blanked to "---" (via
    /// <see cref="DataReference.NormalizeCoordinatePair"/>), so a half-populated pair is
    /// never written into an exported CSV / ZP image. Returns the number of rows changed.
    /// </summary>
    private static int EnforceCoordinatePairs(IEnumerable<DataReference> rows) =>
        rows.Count(r => r.NormalizeCoordinatePair());

    private static void NormalizeZips(IEnumerable<DataReference> rows, ICountryCodeRules? normalizer)
    {
        if (normalizer is null) { return; }
        foreach (var row in rows)
        {
            row.ZpCode = normalizer.Normalize(row.ZpCode);
        }
    }

    private static int EnforceOneDefaultPerZip(IReadOnlyList<DataReference> rows)
    {
        var byCode = new Dictionary<string, List<DataReference>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!byCode.ContainsKey(row.ZpCode)) { byCode[row.ZpCode] = []; }
            byCode[row.ZpCode].Add(row);
        }

        var fixedCount = 0;
        foreach (var (_, group) in byCode)
        {
            var defaultRows = group.Where(r => r.IsDefault).ToList();
            if (defaultRows.Count <= 1) { continue; }

            var keeper = defaultRows
                .OrderBy(r => r.PlaceName ?? "", StringComparer.OrdinalIgnoreCase)
                .Last();

            foreach (var row in defaultRows)
            {
                if (!ReferenceEquals(row, keeper)) { row.IsDefault = false; }
            }

            fixedCount++;
        }

        return fixedCount;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    // Detect and fix admin level 1 name variants before exporting.
    // ── Export pre-check chain ────────────────────────────────────────────────
    // Registered checks run in order before rows are loaded from the DB.
    // Add new IExportPreCheck implementations here to extend the pipeline.
    private static readonly IExportPreCheck[] _refPreChecks =
    [
        new NormalizeAdminNamesCheck(),
        new NormalizePlaceNamesCheck(),
    ];

    private static async Task RunPreChecksAsync(
        System.Data.IDbConnection conn, string ccUpper, string repoRoot)
    {
        var table = new Table()
            .AddColumn("Pre-export check")
            .AddColumn(new TableColumn("Rows fixed").RightAligned());

        var anyFixed = false;
        foreach (var check in _refPreChecks)
        {
            var count = await check.RunAsync(conn, ccUpper, repoRoot);
            if (count > 0)
            {
                table.AddRow(Markup.Escape(check.Name), $"[green]{count}[/]");
                anyFixed = true;
            }
        }

        if (anyFixed)
            AnsiConsole.Write(table);
    }

    private static string AlwaysQuote(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    /// <summary>
    /// Writes a Brotli-compressed copy of the CSV at <paramref name="csvPath"/>
    /// to <c>{csvPath}.br</c> and writes a SHA-256 sidecar for it.
    /// Returns the path of the compressed file.
    /// </summary>
    private static async Task<string> WriteBrotliCompanionAsync(string csvPath)
    {
        var brPath   = csvPath + ".br";
        var csvBytes = await File.ReadAllBytesAsync(csvPath);

        // The write handle must be fully closed before FileHash opens the file for reading.
        // Using an explicit block scope (rather than `await using var`) ensures disposal —
        // and therefore flushing — happens here, not at the end of the method.
        await using (var file = new FileStream(
            brPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1 << 16, useAsync: true))
        {
            await using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize, leaveOpen: false);
            await brotli.WriteAsync(csvBytes);
        }

        await FileHash.WriteSidecarAsync(brPath);

        var rawKb  = csvBytes.Length / 1024.0;
        var brKb   = new FileInfo(brPath).Length / 1024.0;
        Console.WriteLine($"  ✓ Brotli companion: {rawKb:F0} KB → {brKb:F0} KB ({Path.GetFileName(brPath)})");
        return brPath;
    }

    // =========================================================================
    // Argument parsing
    // =========================================================================

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: CountryDataTools export --country CA [--target ref|main|zpi] [--output path] [--curated-only] [--uncompressed] [--from-csv [path]]
                   CountryDataTools export --all [--curated-only]

              --all           Export for all pipeline countries (US, CA, MX) in sequence.
                              With --target ref: backs up source-of-truth reference CSVs for all countries.
                              Without --target (or --target main): exports main + zpi for all countries.
                              Incompatible with --country, --output, --from-csv.
              --country       Country code (required unless --all)
              --target ref    Export full ReferenceDataCSV to Data/{cc}/{cc}.csv (source of truth)
              --target main   Export optimised ZipPostLookupDataCSV (default)
              --target zpi    Export optimised ZP frozen image to Data/{cc}/{cc}.zpi.br
              --output        Override destination file path
              --curated-only  Export only rows where curated = 1
              --uncompressed  For --target zpi: write raw .zpi instead of Brotli .zpi.br
              --from-csv      For --target zpi: rebuild the image from an already-optimised CSV
                              instead of the database (no Docker/SQL needed). Defaults to the
                              committed ZipPostLookup/Data/{cc}/{cc}.csv; pass a path to override.
            """);

    // =========================================================================
    // Private row types
    // =========================================================================

    public enum ExportTarget { Main, Ref, Zpi }
}