using Dapper;
using GeoTimeZone;
using Microsoft.Data.SqlClient;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Models.Counters;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Dsv;
using ZipPostLookup.Normalizers;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools enrichcoords --source "path/to/coords.csv" --country US [--batch 1000] [--dry-run]
///
/// Supports two CSV formats — auto-detected from headers:
///
///   Format A: ZIP,LAT,LNG  (coordinates only)
///     00601,18.180555,-66.749961
///
///   Format B: ZIP,CITY,STATE,LAT,LONG  (coordinates + Name name)
///     "00704","Parc Parque","PR",17.96,-66.22
///
/// Rules:
///   · TimezoneChecked = 0  → resolve via GeoTimeZone, set timezone + TimezoneChecked=1
///   · TimezoneChecked = 1  → skip timezone (already verified)
///   · Format B Name matches [data].[reference] Name (case-insensitive) → set NameChecked=1
///   · No API calls — GeoTimeZone is a pure local lookup
/// </summary>
public static class EnrichReferenceFromCoordinatesCommand
{
    public sealed record Options(string Source, string Country = "US", int BatchSize = 1000, bool DryRun = false);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        var source    = args.OptionValue("--source") ?? "";
        var country   = args.OptionValue("--country", rejectFlagValue: true) ?? "US";
        var batchSize = args.IntOption("--batch", 1000, min: 1);
        var dryRun    = args.HasFlag("--dry-run");

        if (string.IsNullOrWhiteSpace(source)) { PrintUsage(); return 2; }

        if (!File.Exists(source))
        {
            await Console.Error.WriteLineAsync($"  ✗ File not found: {source}");
            return 2;
        }

        return await RunAsync(new Options(source, country, batchSize, dryRun));
    }

    public static async Task<int> RunAsync(Options opts)
    {
        if (!File.Exists(opts.Source))
        {
            await Console.Error.WriteLineAsync($"  ✗ File not found: {opts.Source}");
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

        Console.WriteLine($"Enriching data.reference from coordinates CSV");
        Console.WriteLine($"  Source  : {opts.Source}");
        Console.WriteLine($"  Country : {opts.Country.ToUpperInvariant()}");
        Console.WriteLine($"  Dry run : {opts.DryRun}");
        Console.WriteLine();

        // --- Read CSV (auto-detect format) ---
        var rows = ReadCsv(opts.Source, opts.Country, out var hasCity, out var invalid);
        Console.WriteLine($"  Rows in CSV        : {rows.Count:N0}");
        Console.WriteLine($"  Format detected    : {(hasCity ? "ZIP,CITY,STATE,LAT,LNG" : "ZIP,LAT,LNG")}");
        if (invalid > 0)
            Console.WriteLine($"  Invalid codes      : {invalid:N0} (skipped — failed {opts.Country.ToUpperInvariant()} format check)");

        return await ResolveAndUpdateAsync(opts, db, rows, hasCity);
    }

    // -------------------------------------------------------------------------
    // Explicit column-mapping entry point (dashboard column picker)
    // -------------------------------------------------------------------------

    /// <summary>Zero-based incoming column indices for a mapped coordinate import.</summary>
    public sealed record ColumnIndexes(int Zip, int Lat, int Lng, int? City = null);

    /// <summary>
    /// Runs coordinate enrichment using an explicit column mapping instead of header-name
    /// auto-detection — reads <paramref name="opts"/>.Source with the given
    /// <paramref name="delimiter"/>, skipping the first row when <paramref name="hasHeader"/>
    /// is true. Lets headerless / non-standard files (e.g. GeoNames) be mapped by index.
    /// </summary>
    public static async Task<int> RunAsync(Options opts, ColumnIndexes columns, char delimiter, bool hasHeader)
    {
        if (!File.Exists(opts.Source))
        {
            await Console.Error.WriteLineAsync($"  ✗ File not found: {opts.Source}");
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

        Console.WriteLine("Enriching data.reference from coordinates (mapped columns)");
        Console.WriteLine($"  Source  : {opts.Source}");
        Console.WriteLine($"  Country : {opts.Country.ToUpperInvariant()}");
        Console.WriteLine($"  Dry run : {opts.DryRun}");
        Console.WriteLine();

        var rows = ReadMapped(opts.Source, delimiter, hasHeader, columns, opts.Country, out var hasCity, out var invalid);
        Console.WriteLine($"  Rows parsed        : {rows.Count:N0}");
        Console.WriteLine($"  Mapping            : Zip=col{columns.Zip}, Lat=col{columns.Lat}, Lng=col{columns.Lng}"
            + (columns.City.HasValue ? $", City=col{columns.City}" : ""));
        if (invalid > 0)
            Console.WriteLine($"  Invalid codes      : {invalid:N0} (skipped — failed {opts.Country.ToUpperInvariant()} format check)");

        return await ResolveAndUpdateAsync(opts, db, rows, hasCity);
    }

    // -------------------------------------------------------------------------
    // Resolve timezones + update data.reference (shared by both entry points)
    // -------------------------------------------------------------------------

    private static async Task<int> ResolveAndUpdateAsync(
        Options opts, WorkDbContext db, List<CoordRow> rows, bool hasCity)
    {
        if (rows.Count == 0)
        {
            await Console.Error.WriteLineAsync("  ✗ No rows found — check the file / mapping");
            return 1;
        }

        // --- Resolve timezones locally via GeoTimeZone ---
        Console.WriteLine("  Resolving IANA timezones via GeoTimeZone (local, no API)...");

        var resolved = new List<CoordRow>();
        var failed = 0;

        // Country rules canonicalise retired zone IDs (e.g. America/Nipigon → Toronto) that
        // GeoTimeZone may still emit from an older boundary dataset.
        var countryRules = CountryRules.CountryRulesFactory.For(opts.Country);

        foreach (var row in rows)
        {
            var iana = ResolveTimezone(row.Lat, row.Lng);
            if (iana == null)
            {
                failed++;
            }
            else
            {
                resolved.Add(row with { Timezone = countryRules.CanonicalizeTimezone(iana) });
            }
        }

        Console.WriteLine($"  Resolved           : {resolved.Count:N0}");
        Console.WriteLine($"  Failed (bad coords): {failed:N0}");

        if (opts.DryRun)
        {
            Console.WriteLine();
            Console.WriteLine("  --dry-run: no database updates will be made.");
            Console.WriteLine("  Sample resolved:");
            foreach (var r in resolved.Take(10))
            {
                var cityPart = r.PlaceName != null ? $"  PlaceName:{r.PlaceName}" : "";
                Console.WriteLine($"    {r.ZpCode,-10} → {r.Timezone}{cityPart}");
            }
            return 0;
        }

        // --- Update [data].[reference] in batches ---
        Console.WriteLine();
        Console.WriteLine($"  Updating data.reference in batches of {opts.BatchSize:N0}...");
        Console.WriteLine();

        await using var conn = (SqlConnection)db.GetFactory().CreateConnection();
        var counters = new EnrichCoordinateCounters();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Group by zip — one zip may have many PlaceName rows in Format B
        // For timezone: use first row's resolved timezone per zip
        // For PlaceName: check each PlaceName row against data.reference
        var byZip = resolved
            .GroupBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.ToList(),
                StringComparer.OrdinalIgnoreCase);

        var zipList = byZip.Keys.ToList();

        for (int i = 0; i < zipList.Count; i += opts.BatchSize)
        {
            var batchZips = zipList.Skip(i).Take(opts.BatchSize).ToList();
            var batchRows = batchZips.SelectMany(z => byZip[z]).ToList();

            await UpdateBatchAsync(conn, opts.Country, batchZips, batchRows, hasCity, counters);

            var pct = Math.Min((i + opts.BatchSize) * 100 / zipList.Count, 100);
            var elapsed = stopwatch.Elapsed;
            var rate = (i + opts.BatchSize) / Math.Max(elapsed.TotalSeconds, 1);
            var eta = TimeSpan.FromSeconds(
                Math.Max(zipList.Count - i - opts.BatchSize, 0) / Math.Max(rate, 1));

            Console.Write($"\r  [{pct,3}%] {Math.Min(i + opts.BatchSize, zipList.Count):N0}/{zipList.Count:N0}  " +
                          $"coords_filled:{counters.CoordsFilled:N0}  tz_updated:{counters.TzUpdated:N0}  " +
                          $"tz_skipped:{counters.TzSkipped:N0}  NameChecked:{counters.CityChecked:N0}  ETA:{eta:mm\\:ss}  ");
        }

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("Complete:");
        Console.WriteLine($"  Coordinates filled (missing pair)  : {counters.CoordsFilled:N0}");
        Console.WriteLine($"  Timezone updated (checked=0 → 1)  : {counters.TzUpdated:N0}");
        Console.WriteLine($"  Timezone skipped (already checked) : {counters.TzSkipped:N0}");
        Console.WriteLine($"  Name matched + NameChecked set    : {counters.CityChecked:N0}");
        Console.WriteLine($"  Not in data.reference              : {counters.NotFound:N0}");
        Console.WriteLine($"  Failed to resolve timezone         : {failed:N0}");
        Console.WriteLine($"  Elapsed                            : {stopwatch.Elapsed:mm\\:ss}");

        return 0;
    }

    // -------------------------------------------------------------------------
    // Batch update
    // -------------------------------------------------------------------------

    private static async Task UpdateBatchAsync(
        SqlConnection conn, string country,
        List<string> batchZips,
        List<CoordRow> batchRows,
        bool hasCity,
        EnrichCoordinateCounters counters)
    {
        var cc = country.ToUpperInvariant();

        // Get current state of these zips in data.reference
        var refRows = (await conn.QueryAsync<DataReference>(
            CommonQueries.GetReferenceStateByCodes,
            new { CountryId = cc, Codes = batchZips }))
            .ToList();

        if (refRows.Count == 0)
        {
            counters.NotFound += batchZips.Count;
            return;
        }

        // Build lookup: zip → first resolved timezone for that zip
        var tzByZip = batchRows
            .GroupBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.First().Timezone!,
                StringComparer.OrdinalIgnoreCase);

        // Build lookup: (zip, city_lower) → exists in CSV
        var citySet = hasCity
            ? batchRows
                .Where(r => r.PlaceName != null)
                .Select(r => $"{r.ZpCode}|{r.PlaceName!.ToLowerInvariant()}")
                .ToHashSet()
            : new HashSet<string>();

        // Coordinate back-fill — replace Lat/Lng (as a complete pair) on rows whose existing
        // pair is incomplete. One coord per zip (first row); the SQL WHERE skips complete rows.
        var coordByZip = batchRows
            .GroupBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (coordByZip.Count > 0)
        {
            var coordValues = string.Join(",\n    ",
                coordByZip.Values.Select(r =>
                    $"(N'{EscSql(r.ZpCode)}', N'{Fmt(r.Lat)}', N'{Fmt(r.Lng)}')"));

            var coordSql = string.Format(CommonQueries.UpdateReferenceCoordsBatch, coordValues);
            counters.CoordsFilled += await conn.ExecuteAsync(coordSql, new { CountryId = cc });
        }

        // Timezone update — only for rows where TimezoneChecked = 0
        var tzToUpdate = refRows
            .Where(r => !r.TimezoneChecked && tzByZip.ContainsKey(r.ZpCode))
            .Select(r => (Zip: r.ZpCode, tzByZip[r.ZpCode]))
            .DistinctBy(r => r.Zip)
            .ToList();

        counters.TzSkipped += refRows
            .Where(r => r.TimezoneChecked)
            .Select(r => r.ZpCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (tzToUpdate.Count > 0)
        {
            var tzValues = string.Join(",\n    ",
                tzToUpdate.Select(r => $"(N'{EscSql(r.Zip)}', N'{EscSql(r.Item2)}')"));

            var tzSql = string.Format(CommonQueries.UpdateReferenceTimezoneBatch, tzValues);

            counters.TzUpdated += await conn.ExecuteAsync(tzSql, new { CountryId = cc });
        }

        // PlaceName check update — mark NameChecked=1 where CSV PlaceName matches reference PlaceName
        if (hasCity && citySet.Count > 0)
        {
            var cityMatches = refRows
                .Where(r => !r.NameChecked &&
                            citySet.Contains($"{r.ZpCode}|{r.PlaceName.ToLowerInvariant()}"))
                .Select(r => (Zip: r.ZpCode, City: r.PlaceName))
                .ToList();

            if (cityMatches.Count > 0)
            {
                var cityValues = string.Join(",\n    ",
                    cityMatches.Select(r =>
                        $"(N'{EscSql(r.Zip)}', N'{EscSql(r.City)}')"));

                var citySql = string.Format(CommonQueries.UpdateReferenceNameCheckedBatch, cityValues);

                counters.CityChecked += await conn.ExecuteAsync(citySql, new { CountryId = cc });
            }
        }

        counters.NotFound += batchZips
            .Except(refRows.Select(r => r.ZpCode), StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    // -------------------------------------------------------------------------
    // GeoTimeZone
    // -------------------------------------------------------------------------

    private static string? ResolveTimezone(double lat, double lng)
    {
        try
        {
            var iana = TimeZoneLookup.GetTimeZone(lat, lng).Result;
            return string.IsNullOrWhiteSpace(iana) || !iana.Contains('/') ? null : iana;
        }
        catch
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // CSV reader — auto-detects ZIP,LAT,LNG vs ZIP,CITY,STATE,LAT,LNG
    // -------------------------------------------------------------------------

    private static List<CoordRow> ReadCsv(string path, string country, out bool hasCity, out int invalid)
    {
        hasCity = false;
        invalid = 0;
        var rows  = new List<CoordRow>();
        var rules = GetCodeRules(country);

        // StreamReader with detectEncodingFromByteOrderMarks handles UTF-8 BOM,
        // UTF-16 LE/BE and UTF-32 automatically — no manual byte inspection needed.
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);

        var header = reader.ReadLine();
        if (header == null) { return rows; }

        var cols = header.Split(',');
        int zipIdx = -1, cityIdx = -1, latIdx = -1, lngIdx = -1;

        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i].Trim().Trim('"').ToUpperInvariant();
            switch (col)
            {
                case "ZIP": zipIdx = i; break;
                case "CITY": cityIdx = i; break;
                case "LAT": latIdx = i; break;
                case "LNG" or "LON" or "LONG": lngIdx = i; break;
            }
        }

        if (zipIdx < 0 || latIdx < 0 || lngIdx < 0)
        {
            Console.Error.WriteLine($"  ✗ Could not find ZIP, LAT, LNG columns. Found: {header}");
            return rows;
        }

        hasCity = cityIdx >= 0;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) { continue; }

            var parts = DelimitedFile.SplitLine(line, ',');
            var maxIdx = Math.Max(zipIdx, Math.Max(latIdx, lngIdx));
            if (parts.Length <= maxIdx) { continue; }

            var rawZip = parts[zipIdx].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(rawZip)) { continue; }

            // Normalise to the stored form (CA "A0A 0A1"/"a0a0a1" → "A0A0A1") and validate.
            var zip = rules.Normalize(rawZip);
            if (!rules.Validate(zip)) { invalid++; continue; }

            if (!double.TryParse(parts[latIdx].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lat)) { continue; }

            if (!double.TryParse(parts[lngIdx].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lng)) { continue; }

            string? city = null;
            if (hasCity && cityIdx < parts.Length)
            {
                city = TitleCase(parts[cityIdx].Trim().Trim('"'));
            }

            rows.Add(new CoordRow(zip, lat, lng, city, null));
        }

        return rows;
    }

    /// <summary>
    /// Reads the file with an explicit column mapping (no header-name detection). Skips the
    /// first row when <paramref name="hasHeader"/> is true. Rows shorter than the mapped
    /// indices, or whose Lat/Lng do not parse, are skipped.
    /// </summary>
    private static List<CoordRow> ReadMapped(
        string path, char delimiter, bool hasHeader,
        ColumnIndexes columns, string country, out bool hasCity, out int invalid)
    {
        hasCity = columns.City.HasValue;
        invalid = 0;
        var rows  = new List<CoordRow>();
        var rules = GetCodeRules(country);

        var all    = DelimitedFile.ReadRows(path, delimiter);
        var maxIdx = Math.Max(columns.Zip, Math.Max(columns.Lat, columns.Lng));

        for (int i = 0; i < all.Count; i++)
        {
            if (hasHeader && i == 0) { continue; }

            var parts = all[i];
            if (parts.Length <= maxIdx) { continue; }

            var rawZip = parts[columns.Zip].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(rawZip)) { continue; }

            // Normalise to the stored form (CA "A0A 0A1"/"a0a0a1" → "A0A0A1") and validate.
            var zip = rules.Normalize(rawZip);
            if (!rules.Validate(zip)) { invalid++; continue; }

            if (!double.TryParse(parts[columns.Lat].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lat)) { continue; }

            if (!double.TryParse(parts[columns.Lng].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lng)) { continue; }

            string? city = null;
            if (columns.City is int ci && ci < parts.Length)
                city = TitleCase(parts[ci].Trim().Trim('"'));

            rows.Add(new CoordRow(zip, lat, lng, city, null));
        }

        return rows;
    }

    /// <summary>Per-country postal-code normalizer/validator (matches ImportCodesOnlyCommand).</summary>
    private static ICountryCodeRules GetCodeRules(string country) =>
        country.ToUpperInvariant() switch
        {
            "CA" => new CaCountryCodeRules(),
            "MX" => new MxCountryCodeRules(),
            _    => new UsCountryCodeRules(),
        };

    private static string TitleCase(string input)
    {
        if (string.IsNullOrEmpty(input)) { return input; }
        var words = input.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0) { continue; }
            words[i] = char.ToUpperInvariant(words[i][0]) +
                       words[i][1..].ToLowerInvariant();
        }
        return string.Join(' ', words);
    }

    // -------------------------------------------------------------------------

    private static string EscSql(string s) => s.Replace("'", "''");

    /// <summary>Formats a coordinate for storage (fixed 6dp, invariant culture).</summary>
    private static string Fmt(double d) =>
        d.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools enrichcoords --source "path/to/coords.csv" --country US [--batch 1000] [--dry-run]

              --source    Path to ZIP,LAT,LNG or ZIP,CITY,STATE,LAT,LONG CSV (header required)
              --country   Country code (default: US)
              --batch     Zips per DB batch (default: 1000)
              --dry-run   Show sample without updating DB
            """);

}