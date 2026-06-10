using Dapper;
using GeoTimeZone;
using Microsoft.Data.SqlClient;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Commands;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Dsv;

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
        Console.WriteLine($"  Source  : {source}");
        Console.WriteLine($"  Country : {country.ToUpperInvariant()}");
        Console.WriteLine($"  Dry run : {dryRun}");
        Console.WriteLine();

        // --- Read CSV (auto-detect format) ---
        var rows = ReadCsv(source, out var hasCity);
        Console.WriteLine($"  Rows in CSV        : {rows.Count:N0}");
        Console.WriteLine($"  Format detected    : {(hasCity ? "ZIP,CITY,STATE,LAT,LNG" : "ZIP,LAT,LNG")}");

        if (rows.Count == 0)
        {
            await Console.Error.WriteLineAsync("  ✗ No rows found — check CSV format");
            return 1;
        }

        // --- Resolve timezones locally via GeoTimeZone ---
        Console.WriteLine("  Resolving IANA timezones via GeoTimeZone (local, no API)...");

        var resolved = new List<CoordRow>();
        var failed = 0;

        foreach (var row in rows)
        {
            var iana = ResolveTimezone(row.Lat, row.Lng);
            if (iana == null)
            {
                failed++;
            }
            else
            {
                resolved.Add(row with { Timezone = iana });
            }
        }

        Console.WriteLine($"  Resolved           : {resolved.Count:N0}");
        Console.WriteLine($"  Failed (bad coords): {failed:N0}");

        if (dryRun)
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
        Console.WriteLine($"  Updating data.reference in batches of {batchSize:N0}...");
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

        for (int i = 0; i < zipList.Count; i += batchSize)
        {
            var batchZips = zipList.Skip(i).Take(batchSize).ToList();
            var batchRows = batchZips.SelectMany(z => byZip[z]).ToList();

            await UpdateBatchAsync(conn, country, batchZips, batchRows, hasCity, counters);

            var pct = Math.Min((i + batchSize) * 100 / zipList.Count, 100);
            var elapsed = stopwatch.Elapsed;
            var rate = (i + batchSize) / Math.Max(elapsed.TotalSeconds, 1);
            var eta = TimeSpan.FromSeconds(
                Math.Max(zipList.Count - i - batchSize, 0) / Math.Max(rate, 1));

            Console.Write($"\r  [{pct,3}%] {Math.Min(i + batchSize, zipList.Count):N0}/{zipList.Count:N0}  " +
                          $"tz_updated:{counters.TzUpdated:N0}  tz_skipped:{counters.TzSkipped:N0}  " +
                          $"NameChecked:{counters.CityChecked:N0}  ETA:{eta:mm\\:ss}  ");
        }

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("Complete:");
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
        var refRows = (await conn.QueryAsync<ReferenceStateRow>(
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

    private static List<CoordRow> ReadCsv(string path, out bool hasCity)
    {
        hasCity = false;
        var rows = new List<CoordRow>();

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

            var parts = SplitCsvLine(line);
            var maxIdx = Math.Max(zipIdx, Math.Max(latIdx, lngIdx));
            if (parts.Length <= maxIdx) { continue; }

            var zip = parts[zipIdx].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(zip)) { continue; }

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
    /// Splits a single CSV line respecting RFC 4180 double-quoted fields that
    /// may contain embedded commas or escaped double-quotes.
    /// </summary>
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        int pos = 0;

        while (pos <= line.Length)
        {
            if (pos < line.Length && line[pos] == '"')
            {
                pos++;
                var sb = new System.Text.StringBuilder();
                while (pos < line.Length)
                {
                    if (line[pos] == '"')
                    {
                        pos++;
                        // Escaped double-quote inside a quoted field
                        if (pos < line.Length && line[pos] == '"')
                        {
                            sb.Append('"');
                            pos++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[pos++]);
                    }
                }
                fields.Add(sb.ToString());
                if (pos < line.Length && line[pos] == ',') { pos++; }
            }
            else
            {
                int comma = line.IndexOf(',', pos);
                if (comma < 0)
                {
                    fields.Add(line[pos..]);
                    break;
                }
                fields.Add(line[pos..comma]);
                pos = comma + 1;
            }
        }

        return [.. fields];
    }

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

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools enrichcoords --source "path/to/coords.csv" --country US [--batch 1000] [--dry-run]

              --source    Path to ZIP,LAT,LNG or ZIP,CITY,STATE,LAT,LONG CSV (header required)
              --country   Country code (default: US)
              --batch     Zips per DB batch (default: 1000)
              --dry-run   Show sample without updating DB
            """);

}