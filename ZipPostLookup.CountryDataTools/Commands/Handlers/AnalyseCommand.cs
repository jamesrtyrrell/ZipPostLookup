using System.Text;
using Dapper;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools analyse --country CA [--output report.md]
///
/// Analyses the fully curated rows in [data].[reference] for the given country
/// and writes a structured Markdown report with:
///
///   · Dataset overview (row counts, unique zips, unique names, admin1 breakdown)
///   · Redundancy analysis (duplicate names, shared timezones, missing coords)
///   · Compression opportunity analysis (homogeneous zip groups, range candidates)
///   · Size estimates (raw CSV, stripped, compressed)
///   · AI-ready findings and prompts for each discovered opportunity
///
/// Only runs on countries where data_curated = 1 in [data].[country_info].
/// </summary>
public static class AnalyseCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        if (!TryParseArgs(args, out var country, out var output, out var all))
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
        {
            var exitCode = 0;
            foreach (var cc in new[] { "US", "CA", "MX" })
            {
                Console.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]{cc}[/]").LeftJustified());
                // When --output is a directory, each report goes in {output}/{cc}-analysis-{date}.md
                var ccOutput = string.IsNullOrWhiteSpace(output)
                    ? ""
                    : Path.Combine(output, $"{cc.ToLowerInvariant()}-analysis-{DateTime.UtcNow:yyyyMMdd}.md");
                var result = await RunForCountryAsync(db, cc, ccOutput);
                if (result != 0) exitCode = result;
            }
            return exitCode;
        }

        return await RunForCountryAsync(db, country.ToUpperInvariant(), output);
    }

    private static async Task<int> RunForCountryAsync(WorkDbContext db, string cc, string output)
    {
        using var conn = db.GetFactory().CreateConnection();

        // Guard — only run on curated countries
        var countryInfo = await conn.QueryFirstAsync<DataCountryInfo>(
            CommonQueries.GetCountryInfoById,
            new { CountryId = cc });


        if (string.IsNullOrEmpty(countryInfo.CountryName))
        {
            await Console.Error.WriteLineAsync($"  ✗ Country '{cc}' not found in data.country_info.");
            return 1;
        }

        if (!countryInfo.DataCurated)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ {cc} is not fully curated (data_curated = 0). " +
                $"Run the curation pipeline first.");
            return 1;
        }

        Console.WriteLine($"Analysing {cc} ({countryInfo.CountryName}) — curated data only...");

        // ── Load all curated reference rows + admin1 ──────────────────────────
        Console.WriteLine("  Loading reference rows...");

        var parameters = new DynamicParameters();
        parameters.Add("CountryId", cc);
        
        var sql = CommonQueries.GetAllReferenceDataWithAdmins;

        var dataReferenceEnum = await conn.QueryAsync<DataReference, DataReferenceAdmin, DataReference>
        (sql, (rData, rAdmins) =>
                {
                    rData.AdminReferenceList.Add(rAdmins);
                    return rData;
                },
                new { CountryId = cc },
                splitOn: "ReferenceAdminId"
            );

        var rows = dataReferenceEnum.ToList();
        Console.WriteLine($"  Loaded {rows.Count:N0} rows. Running analysis passes...");
        
        var report = new Report(cc, countryInfo.CountryName, rows);
        report.Run();

        // ── Write output ──────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(output))
        {
            var dataAnalysisPath = Path.Combine(Directory.GetCurrentDirectory(), "DataAnalysis");
            if (!Directory.Exists(dataAnalysisPath))
            {
                Directory.CreateDirectory(dataAnalysisPath);
            }

            output = Path.Combine(dataAnalysisPath,
                $"{cc.ToLowerInvariant()}-analysis-{DateTime.UtcNow:yyyyMMdd}.md");
        }

        await File.WriteAllTextAsync(output, report.Build());

        Console.WriteLine($"  ✓ Report written to: {output}");
        return 0;
    }

    // =========================================================================
    // Report builder
    // =========================================================================

    private sealed class Report
    {
        private readonly string _cc;
        private readonly string _countryName;
        private readonly List<DataReference> _rows;
        private readonly List<Finding> _findings = [];
        private readonly StringBuilder _sb = new();

        // ── Computed stats ────────────────────────────────────────────────────
        private int _totalRows;
        private int _uniqueZips;
        private int _uniqueNames;
        private int _uniqueTimezones;
        private int _uniqueAdmin1;
        private int _rowsWithCoords;
        private int _rowsWithoutCoords;
        private int _rowsDefaultOnly;
        private int _multiNameZips;

        // Compression
        private int _homogeneousGroups;
        private int _homogeneousRows;
        private int _heterogeneousRows;

        // Redundancy
        private int _timezoneIndexSaving;
        private int _admin1IndexSaving;

        // Size estimates (bytes)
        private long _estimatedRawBytes;
        private long _estimatedStrippedBytes;
        private long _estimatedIndexedBytes;
        private long _estimatedCompressedBytes;

        public Report(string cc, string countryName, List<DataReference> rows)
        {
            _cc = cc;
            _countryName = countryName;
            _rows = rows;
        }

        public void Run()
        {
            _totalRows = _rows.Count;
            _uniqueZips = _rows.Select(r => r.ZpCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _uniqueNames = _rows.Select(r => r.PlaceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _uniqueTimezones = _rows.Select(r => r.Timezone).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _uniqueAdmin1 = _rows.Select(r => r.Admin1Code).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _rowsWithCoords = _rows.Count(r => r.Lat != "---" && !string.IsNullOrEmpty(r.Lat));
            _rowsWithoutCoords = _totalRows - _rowsWithCoords;

            // Zips with multiple names vs single
            var byZip = _rows.GroupBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase).ToList();
            _rowsDefaultOnly = byZip.Count(g => g.Count() == 1);
            _multiNameZips = byZip.Count(g => g.Count() > 1);

            // ── Compression pass ─────────────────────────────────────────────
            // Group by prefix — the natural range unit per country:
            //   CA: first 3 chars (FSA)
            //   MX: first 2 chars (INEGI state block)
            //   US: first 3 chars (3-digit ZIP prefix / SCF)
            //   Default: first 3 chars
            var prefixLen = _cc switch
            {
                "MX" => 2,
                _ => 3
            };

            var byPrefix = _rows
                .GroupBy(r => r.ZpCode!.Length >= prefixLen
                    ? r.ZpCode[..prefixLen]
                    : r.ZpCode,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in byPrefix)
            {
                var distinct = group
                    .Select(r => (r.PlaceName, r.Timezone, r.Admin1, r.Admin1Code))
                    .Distinct()
                    .ToList();

                if (distinct.Count == 1)
                {
                    _homogeneousGroups++;
                    _homogeneousRows += group.Count();
                }
                else
                {
                    _heterogeneousRows += group.Count();
                }
            }

            // ── Redundancy pass ──────────────────────────────────────────────
            // Timezone indexing: every row stores the full IANA string (~20 chars avg)
            // If we store a 1-byte index instead, saving = (avgLen - 1) * totalRows
            var avgTzLen = _rows.Average(r => r.Timezone.Length);
            _timezoneIndexSaving = (int)((avgTzLen - 1) * _totalRows);

            // Admin1 indexing: same idea
            var avgAdmin1Len = _rows.Average(r => (r.Admin1.Length) + (r.Admin1Code.Length) + 1);
            _admin1IndexSaving = (int)((avgAdmin1Len - 2) * _totalRows);

            // ── Size estimates ───────────────────────────────────────────────
            // Rough byte estimates based on average row width
            var avgRowBytes = _rows.Take(1000).Average(r =>
                (r.ZpCode?.Length ?? 0) + (r.PlaceName?.Length ?? 0) + (r.Timezone?.Length ?? 0) +
                (r.Admin1?.Length ?? 0) + (r.Admin1Code?.Length ?? 0) +
                10 /* is_default, lat, lng placeholders, commas, quotes, newline */);

            _estimatedRawBytes = (long)(avgRowBytes * _totalRows);
            _estimatedStrippedBytes = (long)((avgRowBytes - 12) * _totalRows); // -lat/-lng
            _estimatedIndexedBytes = _estimatedStrippedBytes
                                      - _timezoneIndexSaving
                                      - _admin1IndexSaving;

            // Brotli typically achieves ~85% compression on repetitive CSV data
            _estimatedCompressedBytes = (long)(_estimatedIndexedBytes * 0.15);

            // ── Findings ─────────────────────────────────────────────────────
            BuildFindings();
        }

        private void BuildFindings()
        {
            // Rows saved = homogeneousRows - homogeneousGroups
            // (each homogeneous group of N rows collapses to 1 range row)
            var rowsSaved = _homogeneousRows - _homogeneousGroups;
            var compressionPct = _totalRows == 0 ? 0
                : rowsSaved * 100.0 / _totalRows;

            // Finding 1: Range compression
            if (_homogeneousGroups > 0)
            {
                _findings.Add(new Finding(
                    Id: "F01",
                    Category: "Compression",
                    Title: "Range notation for homogeneous zip groups",
                    Severity: compressionPct >= 50 ? "High" : compressionPct >= 20 ? "Medium" : "Low",
                    Summary:
                        $"{_homogeneousGroups:N0} prefix groups are homogeneous — all rows " +
                        $"within the group share the same name, timezone, and admin1. " +
                        $"These {_homogeneousRows:N0} rows could collapse to " +
                        $"{_homogeneousGroups:N0} range rows, saving " +
                        $"{rowsSaved:N0} rows ({compressionPct:F1}% reduction). " +
                        $"{_heterogeneousRows:N0} rows in mixed groups must stay individual.",
                    AiPrompt:
                        $"For {_cc} postal code data: {_homogeneousGroups:N0} zip codes are " +
                        $"homogeneous — every place name, timezone, and admin1 value is identical " +
                        $"across all rows for that zip. Propose a range notation format for the " +
                        $"embedded CSV that collapses these into single rows while remaining " +
                        $"parseable by the existing BuiltInDataSource CSV reader. Consider: " +
                        $"(1) what the range key should encode, (2) how the reader detects a " +
                        $"range row vs an exact row, (3) whether the ZipRegistry range index " +
                        $"needs changes, and (4) edge cases like partial matches."
                ));
            }

            // Finding 2: Lat/lng size saving (always reported — coords are always stripped on export)
            {
                var coordSavingKb = (_estimatedRawBytes - _estimatedStrippedBytes) / 1024;
                var pctWithCoords = _rowsWithCoords * 100.0 / _totalRows;
                _findings.Add(new Finding(
                    Id: "F02",
                    Category: "Size Reduction",
                    Title: "Lat/lng columns stripped unconditionally from release CSV",
                    Severity: "Info",
                    Summary:
                        $"Coordinates are stored in data.reference for enrichment purposes but are " +
                        $"not used by ZipPostLookup at runtime — only timezones and admin1 are consumed. " +
                        $"The export pipeline always strips lat/lng, saving " +
                        $"~{coordSavingKb:N0} KB. " +
                        $"{_rowsWithCoords:N0} of {_totalRows:N0} rows ({pctWithCoords:F1}%) " +
                        $"have coordinate data in data.reference for reference purposes.",
                    AiPrompt:
                        $"The {_cc} release CSV has lat/lng columns stripped unconditionally by the " +
                        $"export pipeline because ZipPostLookup only uses timezones and admin1 at runtime. " +
                        $"{_rowsWithCoords:N0} of {_totalRows:N0} rows ({pctWithCoords:F1}%) have real " +
                        $"coordinates stored in data.reference. If a future version of the library needs " +
                        $"coordinates, consider: (1) a separate opt-in coordinates CSV embedded only when " +
                        $"requested, (2) a nullable lat/lng extension on CodeEntry, and (3) the impact on " +
                        $"binary size for the default no-coordinates path."
                ));
            }

            // Finding 3: Timezone indexing
            if (_uniqueTimezones <= 20 && _totalRows > 10_000)
            {
                _findings.Add(new Finding(
                    Id: "F03",
                    Category: "Size Reduction",
                    Title: "Timezone column is highly repetitive — index it",
                    Severity: _uniqueTimezones <= 5 ? "High" : "Medium",
                    Summary:
                        $"Only {_uniqueTimezones} unique IANA timezone strings across {_totalRows:N0} rows. " +
                        $"The full timezone string (avg ~{_rows.Average(r => r.Timezone.Length):F0} chars) " +
                        $"is repeated on every row. Replacing with a single-char index byte and a " +
                        $"header lookup table would save ~{_timezoneIndexSaving / 1024:N0} KB.",
                    AiPrompt:
                        $"The {_cc} CSV has {_uniqueTimezones} unique timezones across {_totalRows:N0} rows. " +
                        $"Unique values: {string.Join(", ", _rows.Select(r => r.Timezone).Distinct().OrderBy(t => t))}. " +
                        $"Propose a header-encoded index format where the first line of the CSV " +
                        $"encodes the timezone lookup table (e.g. #tz:0=America/Toronto,1=America/Vancouver) " +
                        $"and each data row uses the index digit instead of the full string. " +
                        $"Consider: (1) backward compatibility with the existing parser, " +
                        $"(2) how BuiltInDataSource would detect and decode the header, " +
                        $"(3) whether the same technique applies to admin1/Admin1Code."
                ));
            }

            // Finding 4: Admin1 indexing
            if (_uniqueAdmin1 <= 50 && _totalRows > 10_000)
            {
                _findings.Add(new Finding(
                    Id: "F04",
                    Category: "Size Reduction",
                    Title: "Admin1 columns are highly repetitive — index them",
                    Severity: _uniqueAdmin1 <= 15 ? "High" : "Medium",
                    Summary:
                        $"Only {_uniqueAdmin1} unique admin1 divisions across {_totalRows:N0} rows. " +
                        $"Both the admin1 name and Admin1Code are repeated verbatim on every row. " +
                        $"Indexing them would save ~{_admin1IndexSaving / 1024:N0} KB.",
                    AiPrompt:
                        $"The {_cc} CSV has {_uniqueAdmin1} unique admin1 divisions repeated across " +
                        $"{_totalRows:N0} rows. Consider combining the timezone and admin1 index " +
                        $"techniques from F03 into a single header metadata block. How would the " +
                        $"BuiltInDataSource parser handle multiple index types in a single header? " +
                        $"Would a JSON metadata header line be cleaner than a custom format? " +
                        $"What are the tradeoffs between parse speed and size reduction?"
                ));
            }

            // Finding 5: Multi-name zips
            if (_multiNameZips > 0)
            {
                var avgNamesPerMulti = _rows
                    .GroupBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Average(g => (double)g.Count());

                _findings.Add(new Finding(
                    Id: "F05",
                    Category: "Data Structure",
                    Title: "Multi-name zip codes — consider name list encoding",
                    Severity: "Low",
                    Summary:
                        $"{_multiNameZips:N0} zip codes have multiple place names " +
                        $"(avg {avgNamesPerMulti:F1} names per zip). These are stored as " +
                        $"separate rows sharing the same zip, timezone, and admin1. " +
                        $"A pipe-delimited names column on a single row per zip would " +
                        $"reduce row count but complicate lookups by name.",
                    AiPrompt:
                        $"In {_cc} data, {_multiNameZips:N0} zip codes have multiple place names " +
                        $"(colonias, barrios, etc.) stored as separate rows. The current model " +
                        $"stores one row per name. Analyse the tradeoffs of: (1) keeping the " +
                        $"current one-row-per-name model, (2) a pipe-delimited names column " +
                        $"('Colonia A|Colonia B|Colonia C'), and (3) a separate names lookup " +
                        $"table keyed by zip. Consider impact on GetByCity(), GetByZip() " +
                        $"returning IsDefault correctly, and library binary size."
                ));
            }

            // Finding 6: Brotli compression
            _findings.Add(new Finding(
                Id: "F06",
                Category: "Compression",
                Title: "Brotli stream compression of embedded CSV",
                Severity: "Medium",
                Summary:
                    $"Estimated raw CSV size: ~{_estimatedRawBytes / 1024:N0} KB. " +
                    $"After stripping lat/lng and indexing timezones/admin1: " +
                    $"~{_estimatedIndexedBytes / 1024:N0} KB. " +
                    $"After Brotli compression: ~{_estimatedCompressedBytes / 1024:N0} KB " +
                    $"(estimated ~85% compression ratio on repetitive CSV). " +
                    $"System.IO.Compression.BrotliStream is available in .NET base — no NuGet needed.",
                AiPrompt:
                    $"The {_cc} embedded CSV is estimated at ~{_estimatedRawBytes / 1024:N0} KB. " +
                    $"Propose an implementation for compressing it with BrotliStream at build time " +
                    $"in ReleaseOptimiserTools, and decompressing lazily on first access in " +
                    $"BuiltInDataSource. Specifically: (1) how to embed a .br file as a resource " +
                    $"in the .csproj, (2) the decompression path in BuiltInDataSource.GetEntries(), " +
                    $"(3) thread-safety of the lazy decompression cache, and (4) whether the " +
                    $"decompressed stream should be kept in memory or re-decompressed each call. " +
                    $"Assume .NET 8+ target framework."
            ));
        }

        public string Build()
        {
            var now = DateTime.UtcNow;
            _sb.Clear();

            // ── Header ────────────────────────────────────────────────────────
            Line($"# ZipPostLookup — Data Analysis Report: {_cc}");
            Line();
            Line($"**Country:** {_countryName} (`{_cc}`)  ");
            Line($"**Generated:** {now:yyyy-MM-dd HH:mm} UTC  ");
            Line($"**Source:** `data.reference` (curated rows only)  ");
            Line($"**Total rows analysed:** {_totalRows:N0}  ");
            Line();
            Line("---");
            Line();

            // ── Dataset Overview ──────────────────────────────────────────────
            Line("## 1. Dataset Overview");
            Line();
            Line("| Metric | Value |");
            Line("|--------|-------|");
            Line($"| Total rows (curated) | {_totalRows:N0} |");
            Line($"| Unique zip codes | {_uniqueZips:N0} |");
            Line($"| Unique place names | {_uniqueNames:N0} |");
            Line($"| Unique IANA timezones | {_uniqueTimezones} |");
            Line($"| Unique admin1 divisions | {_uniqueAdmin1} |");
            Line($"| Rows with coordinates | {_rowsWithCoords:N0} ({_rowsWithCoords * 100.0 / _totalRows:F1}%) |");
            Line($"| Rows without coordinates | {_rowsWithoutCoords:N0} ({_rowsWithoutCoords * 100.0 / _totalRows:F1}%) |");
            Line($"| Single-name zip codes | {_rowsDefaultOnly:N0} |");
            Line($"| Multi-name zip codes | {_multiNameZips:N0} |");
            Line();

            // ── Timezone breakdown ────────────────────────────────────────────
            Line("### Timezone Distribution");
            Line();
            Line("| Timezone | Rows | % |");
            Line("|----------|------|---|");
            foreach (var tz in _rows
                .GroupBy(r => r.Timezone, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()))
            {
                Line($"| `{tz.Key}` | {tz.Count():N0} | {tz.Count() * 100.0 / _totalRows:F1}% |");
            }
            Line();

            // ── Admin1 breakdown ──────────────────────────────────────────────
            Line("### Admin1 Division Distribution");
            Line();
            Line("| Code | Name | Rows | Unique Zips |");
            Line("|------|------|------|-------------|");
            foreach (var div in _rows
                .GroupBy(r => r.Admin1Code, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()))
            {
                var name = div.First().Admin1;
                var zipCount = div.Select(r => r.ZpCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                Line($"| `{div.Key}` | {name} | {div.Count():N0} | {zipCount:N0} |");
            }
            Line();

            // ── Size Estimates ────────────────────────────────────────────────
            Line("## 2. Size Estimates");
            Line();
            Line("Estimates are based on average row character widths sampled from the dataset.");
            Line();
            Line("| Variant | Est. Size | Notes |");
            Line("|---------|-----------|-------|");
            Line($"| Raw reference CSV | ~{_estimatedRawBytes / 1024:N0} KB | All columns including lat/lng (data.reference) |");
            Line($"| Stripped (no lat/lng) | ~{_estimatedStrippedBytes / 1024:N0} KB | Always applied — coords not used by ZipPostLookup |");
            Line($"| Indexed (tz + admin1) | ~{_estimatedIndexedBytes / 1024:N0} KB | Replace repeated strings with index |");
            Line($"| Brotli compressed | ~{_estimatedCompressedBytes / 1024:N0} KB | ~85% ratio on repetitive CSV |");
            Line();

            // ── Compression Opportunities ─────────────────────────────────────
            Line("## 3. Compression Opportunities");
            Line();
            Line("| Metric | Value |");
            Line("|--------|-------|");
            Line($"| Homogeneous zip groups (range candidates) | {_homogeneousGroups:N0} |");
            Line($"| Rows representable as range rows | {_homogeneousRows:N0} |");
            Line($"| Heterogeneous rows (must stay individual) | {_heterogeneousRows:N0} |");
            Line($"| Potential range compression | {(_totalRows > 0 ? (_homogeneousRows - _homogeneousGroups) * 100.0 / _totalRows : 0):F1}% row reduction |");
            Line();

            // ── Findings ─────────────────────────────────────────────────────
            Line("## 4. Findings");
            Line();
            Line("Each finding includes an AI prompt you can paste directly into Claude or another");
            Line("AI assistant to get implementation suggestions tailored to this dataset.");
            Line();

            foreach (var f in _findings)
            {
                var badge = f.Severity switch
                {
                    "High" => "🔴 High",
                    "Medium" => "🟡 Medium",
                    _ => "🟢 Low"
                };

                Line($"### {f.Id} — {f.Title}");
                Line();
                Line($"**Category:** {f.Category}  ");
                Line($"**Priority:** {badge}  ");
                Line();
                Line($"**Finding:**  ");
                Line(f.Summary);
                Line();
                Line("**AI Prompt:**");
                Line();
                Line("```");
                Line(f.AiPrompt);
                Line("```");
                Line();
            }

            // ── Summary Prompt ────────────────────────────────────────────────
            Line("## 5. Combined Optimisation Prompt");
            Line();
            Line("Paste this into an AI assistant for a holistic optimisation plan:");
            Line();
            Line("```");
            Line($"I have a postal code library (ZipPostLookup, .NET 8) with embedded CSV data");
            Line($"for {_countryName} ({_cc}). The dataset has {_totalRows:N0} rows, {_uniqueZips:N0} unique");
            Line($"zip codes, {_uniqueTimezones} unique timezones, and {_uniqueAdmin1} admin1 divisions.");
            Line($"The estimated raw CSV size is ~{_estimatedRawBytes / 1024:N0} KB.");
            Line();
            Line($"Key characteristics:");
            Line($"- Lat/lng always stripped from release CSV (not used by ZipPostLookup at runtime)");
            Line($"- {_homogeneousGroups:N0} zip codes are homogeneous (same name/tz/admin1 for all entries)");
            Line($"- Only {_uniqueTimezones} unique IANA timezone strings repeated across all rows");
            Line($"- Only {_uniqueAdmin1} unique admin1 divisions repeated across all rows");
            Line($"- {_multiNameZips:N0} zip codes have multiple place names");
            Line();
            Line($"The library uses a BuiltInDataSource class that reads the embedded CSV line by");
            Line($"line into a ZipRegistry (FrozenDictionary indexes). The ReleaseOptimiserTools");
            Line($"project runs pre-pack transformations. System.IO.Compression is available.");
            Line();
            Line($"Please propose a prioritised optimisation plan covering: range compression,");
            Line($"string indexing, and Brotli compression. For each,");
            Line($"estimate the size saving and implementation complexity. Assume the public");
            Line($"API (ZipEntry, ZipRegistry) must not change.");
            Line("```");
            Line();
            Line("---");
            Line();
            Line($"*Report generated by `CountryDataTools analyse --country {_cc}` on {now:yyyy-MM-dd}.*");

            return _sb.ToString();
        }

        private void Line(string text = "") => _sb.AppendLine(text);
    }

    // =========================================================================
    // Supporting types
    // =========================================================================

    private sealed record Finding(
        string Id,
        string Category,
        string Title,
        string Severity,
        string Summary,
        string AiPrompt
    );

    // =========================================================================
    // Arg parsing
    // =========================================================================

    private static bool TryParseArgs(string[] args, out string country, out string output, out bool all)
    {
        country = "";
        output  = "";
        all     = false;

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
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
            }
        }

        return all || !string.IsNullOrWhiteSpace(country);
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools analyse --country CA [--output report.md]
                or countrydatatools analyse --all        [--output dir/]

              --country XX   Country code (US / CA / MX) — must be fully curated
              --all          Run for all pipeline countries (US, CA, MX) in sequence
              --output       Report path (single) or directory (--all); default: DataAnalysis/
            """);
}