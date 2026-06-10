using System.Globalization;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.DSV;
using ZipPostLookup.CountryDataTools.Models.Dsv;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools convert &lt;file.tsv&gt; [--country XX] [--output path/to/output.csv] [--no-prompts]
///
/// Converts a known third-party postal-code data file into the standard
/// ZipPostLookup candidate CSV format:
///
///   code,name,timezone,is_default,lat,lng,admin1,Admin1Code
///
/// Supported formats (auto-detected):
///
///   GeoNames postal code TSV
///     Tab-delimited, 12 columns.
///     country_code, postal_code, place_name, admin_name1, admin_code1,
///     admin_name2, admin_code2, admin_name3, admin_code3, latitude,
///     longitude, accuracy
///     First-line detection: column 0 is a 2-letter ISO country code AND
///     column 9 looks like a latitude (numeric, abs ≤ 90).
///
///   OpenStreetMap streets TSV (suburb/country/state/… /postal_code/street_name)
///     Tab-delimited with a header row.
///     Yields one output row per unique postal_code + city (no lat/lng).
///     First-line detection: header contains "postal_code" and "street_name".
///
///   OpenStreetMap addresses TSV (postal_code_/city_/street_/x_min/…)
///     Tab-delimited with a header row.
///     Yields one output row per unique postal_code_ + city_ (with lat/lng from x/y midpoints).
///     First-line detection: header contains "postal_code_" and "city_".
///
///   OpenStreetMap houses TSV (postal_code/city/street/house_number/x/y/country)
///     Tab-delimited with a header row.
///     Yields one output row per unique postal_code + city (with lat/lng from x/y averages).
///     First-line detection: header contains "postal_code" and "house_number".
///
/// The country code is derived from the filename prefix (e.g. us-streets-openstreetmap.tsv → US)
/// or the --country flag; for GeoNames files it can also be read from column 0.
///
/// Output file is written alongside the source file as {cc}-candidate.csv unless --output is given.
///
/// OPTIONS
///   --country XX     Override country code (default: derived from filename or data)
///   --output path    Override output file path
///   --no-prompts     Skip the format confirmation prompt (auto-confirm)
/// </summary>
public static class ConvertKnownFormatsCommand
{
    // =========================================================================
    // Entry point
    // =========================================================================

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        var inputFile       = args.Length > 0 ? args[0] : "";
        var countryOverride = args.OptionValue("--country", rejectFlagValue: true);
        var outputOverride  = args.OptionValue("--output");
        var noPrompts       = args.HasFlag("--no-prompts");

        if (string.IsNullOrWhiteSpace(inputFile)) { PrintUsage(); return 2; }

        if (!File.Exists(inputFile))
        {
            await Console.Error.WriteLineAsync($"  ✗ File not found: {inputFile}");
            return 2;
        }

        // ── Detect format ─────────────────────────────────────────────────────
        string firstLine;
        using (var sr = new StreamReader(inputFile, detectEncodingFromByteOrderMarks: true))
            firstLine = sr.ReadLine() ?? "";

        var format = FindFormat(firstLine);
        if (format is null)
        {
            await Console.Error.WriteLineAsync(
                "  ✗ Could not detect a known format in this file.");
            await Console.Error.WriteLineAsync(
                "    Supported: GeoNames TSV, OpenStreetMap streets/addresses/houses TSV.");
            return 2;
        }

        Console.WriteLine($"  Detected format : {format.Value.Name}");

        // ── Confirm with user unless --no-prompts ─────────────────────────────
        if (!noPrompts)
        {
            if (!AnsiConsole.Confirm($"Can you confirm this is a {Markup.Escape(format.Value.Name)} file?"))
            {
                Console.WriteLine("  Cancelled.");
                return 0;
            }
        }

        // ── Resolve country code ──────────────────────────────────────────────
        var country = ResolveCountry(inputFile, countryOverride, format.Value.Name);
        if (string.IsNullOrWhiteSpace(country))
        {
            await Console.Error.WriteLineAsync(
                "  ✗ Cannot determine country code. " +
                "Rename the file to start with {cc}- (e.g. us-geonames.tsv) or pass --country XX.");
            return 2;
        }

        Console.WriteLine($"  Country code    : {country}");

        // ── Resolve output path ───────────────────────────────────────────────
        var outputPath = ResolveOutputPath(inputFile, outputOverride, country);
        Console.WriteLine($"  Output          : {outputPath}");
        Console.WriteLine();

        // ── Convert ───────────────────────────────────────────────────────────
        int written;
        try
        {
            written = await format.Value.Convert(inputFile, outputPath, country);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"  ✗ Conversion failed: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"  ✓ Written {written:N0} rows → {outputPath}");
        Console.WriteLine();
        Console.WriteLine("  Next step: countrydatatools validate <output.csv> --country " + country);
        return 0;
    }

    // =========================================================================
    // Format registry — each TSV model owns its detection heuristic and name.
    // Order matters: OsmAddresses before OsmHouses/Streets (trailing _ is most
    // specific); GeoNames last (data heuristic, no header).
    // =========================================================================

    private static readonly (
        Func<string, bool>                       Detect,
        string                                   Name,
        Func<string, string, string, Task<int>>  Convert)[]
        _formats =
    [
        (OsmAddressesDataTSV.Detect, OsmAddressesDataTSV.FormatName, ConvertOsmAddressesAsync),
        (OsmHousesDataTSV.Detect,    OsmHousesDataTSV.FormatName,    ConvertOsmHousesAsync),
        (OsmStreetsDataTSV.Detect,    OsmStreetsDataTSV.FormatName,    ConvertOsmStreetsAsync),
        (GeoNamesDataTSV.Detect,     GeoNamesDataTSV.FormatName,     ConvertGeoNamesAsync),
    ];

    private static (Func<string, bool> Detect, string Name, Func<string, string, string, Task<int>> Convert)?
        FindFormat(string firstLine)
    {
        foreach (var f in _formats)
            if (f.Detect(firstLine)) return f;
        return null;
    }

    // =========================================================================
    // Country code resolution
    // =========================================================================

    private static string ResolveCountry(string filePath, string? explicitCountry, string formatName)
    {
        if (!string.IsNullOrWhiteSpace(explicitCountry))
            return explicitCountry.ToUpperInvariant();

        // Try filename prefix: us-streets-… or us_geonames etc.
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (stem.Length >= 2)
        {
            var prefix = stem[..2];
            if (prefix.All(char.IsLetter))
                return prefix.ToUpperInvariant();
        }

        // GeoNames files embed the country code in column 0 of the first data row.
        if (formatName == GeoNamesDataTSV.FormatName)
        {
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            var first = sr.ReadLine() ?? "";
            var col0  = first.Split('\t')[0].Trim();
            if (col0.Length == 2 && col0.All(char.IsLetter))
                return col0.ToUpperInvariant();
        }

        return "";
    }

    private static string ResolveOutputPath(string inputFile, string? outputOverride, string country)
    {
        if (!string.IsNullOrWhiteSpace(outputOverride))
        {
            return outputOverride;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(inputFile)) ?? Directory.GetCurrentDirectory();
        return Path.Combine(dir, $"{country.ToLowerInvariant()}-candidate.csv");
    }

    // =========================================================================
    // GeoNames converter
    //
    // Input columns (tab-delimited, no header):
    //   0  country_code
    //   1  postal_code
    //   2  place_name
    //   3  admin_name1  (state/province)
    //   4  admin_code1
    //   5  admin_name2
    //   6  admin_code2
    //   7  admin_name3
    //   8  admin_code3
    //   9  latitude
    //  10  longitude
    //  11  accuracy
    // =========================================================================

    private static async Task<int> ConvertGeoNamesAsync(
        string inputPath, string outputPath, string country)
    {
        // Group by postal_code — GeoNames can have multiple place names per code.
        // We keep one row per (postal_code, place_name) pair.
        // The first entry encountered per postal_code is flagged is_default=true.

        var seen        = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var outputRows  = new List<CsvRow>();
        int lineNum     = 0;
        int skipped     = 0;

        using (var sr = new StreamReader(inputPath, detectEncodingFromByteOrderMarks: true))
        {
            string? line;
            while ((line = await sr.ReadLineAsync()) != null)
            {
                lineNum++;
                if (string.IsNullOrWhiteSpace(line)) { continue; }

                var cols = line.Split('\t');
                if (cols.Length < 11)
                {
                    skipped++;
                    continue;
                }

                var postalCode = cols[1].Trim();
                var placeName  = cols[2].Trim();
                var admin1     = cols[3].Trim();
                var admin1Code = cols[4].Trim();
                var latStr     = cols[9].Trim();
                var lngStr     = cols[10].Trim();

                if (string.IsNullOrWhiteSpace(postalCode) || string.IsNullOrWhiteSpace(placeName))
                {
                    skipped++;
                    continue;
                }

                var isDefault = !seen.ContainsKey(postalCode);
                if (isDefault) { seen[postalCode] = true; }

                outputRows.Add(new CsvRow
                {
                    RecordNumber = lineNum,
                    ZpCode       = postalCode,
                    PlaceName    = placeName,
                    Timezone     = "---",
                    IsDefault    = isDefault ? "true" : "false",
                    Lat          = string.IsNullOrWhiteSpace(latStr)  ? "---" : latStr,
                    Lng          = string.IsNullOrWhiteSpace(lngStr)  ? "---" : lngStr,
                    Admin1       = string.IsNullOrWhiteSpace(admin1)  ? "---" : admin1,
                    Admin1Code   = string.IsNullOrWhiteSpace(admin1Code) ? "---" : admin1Code,
                });
            }
        }

        if (skipped > 0)
        {
            Console.WriteLine($"  ⚠ Skipped {skipped:N0} malformed/empty lines.");
        }

        WriteCandidateCsv(outputRows, outputPath);
        return outputRows.Count;
    }

    // =========================================================================
    // OpenStreetMap Streets converter
    //
    // Input header (tab or comma delimited):
    //   suburb, country, state, [province,] city, [district,] postal_code, street_name
    //
    // No lat/lng available. Yields one row per unique (postal_code, city).
    // =========================================================================

    private static async Task<int> ConvertOsmStreetsAsync(
        string inputPath, string outputPath, string country)
    {
        string? headerLine;
        using (var sr = new StreamReader(inputPath, detectEncodingFromByteOrderMarks: true))
        {
            headerLine = await sr.ReadLineAsync();
        }

        if (headerLine == null)
        {
            throw new InvalidDataException("File is empty.");
        }

        // Detect delimiter (tab or comma)
        var delimiter = headerLine.Contains('\t') ? '\t' : ',';
        var headers   = headerLine.Split(delimiter)
            .Select(h => h.Trim().ToLowerInvariant())
            .ToArray();

        int idxPostal = IndexOf(headers, "postal_code");
        int idxCity   = IndexOf(headers, "city");
        int idxState  = IndexOf(headers, "state");
        int idxProv   = IndexOf(headers, "province");   // CA uses province instead of state

        if (idxPostal < 0)
        {
            throw new InvalidDataException("postal_code column not found.");
        }

        // admin1 comes from state or province column
        int idxAdmin = idxState >= 0 ? idxState : idxProv;

        var seen       = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var outputRows = new List<CsvRow>();
        int lineNum    = 1;
        int skipped    = 0;

        using (var sr = new StreamReader(inputPath, detectEncodingFromByteOrderMarks: true))
        {
            await sr.ReadLineAsync(); // skip header
            string? line;
            while ((line = await sr.ReadLineAsync()) != null)
            {
                lineNum++;
                if (string.IsNullOrWhiteSpace(line)) { continue; }

                var cols = line.Split(delimiter);

                var postalCode = SafeCol(cols, idxPostal);
                var city       = SafeCol(cols, idxCity);
                var admin1     = idxAdmin >= 0 ? SafeCol(cols, idxAdmin) : "---";

                if (string.IsNullOrWhiteSpace(postalCode) || string.IsNullOrWhiteSpace(city))
                {
                    skipped++;
                    continue;
                }

                // Deduplicate on (postal_code + city)
                var key = $"{postalCode}|{city}";
                if (seen.ContainsKey(key)) { continue; }
                seen[key] = true;

                var isDefault = !seen.ContainsKey(postalCode + "|__default__");
                if (isDefault) { seen[postalCode + "|__default__"] = true; }

                outputRows.Add(new CsvRow
                {
                    RecordNumber = lineNum,
                    ZpCode       = postalCode,
                    PlaceName    = city,
                    Timezone     = "---",
                    IsDefault    = isDefault ? "true" : "false",
                    Lat          = "---",
                    Lng          = "---",
                    Admin1       = string.IsNullOrWhiteSpace(admin1) ? "---" : admin1,
                    Admin1Code   = "---",
                });
            }
        }

        if (skipped > 0)
        {
            Console.WriteLine($"  ⚠ Skipped {skipped:N0} rows with missing postal_code or city.");
        }

        WriteCandidateCsv(outputRows, outputPath);
        return outputRows.Count;
    }

    // =========================================================================
    // OpenStreetMap Addresses converter
    //
    // Input header (tab-delimited):
    //   postal_code_, city_, street_, x_min, x_max, y_min, y_max,
    //   house_min, house_max, house_odd, house_even
    //
    // lat = average of y_min and y_max; lng = average of x_min and x_max.
    // Yields one row per unique (postal_code_, city_).
    // =========================================================================

    private static async Task<int> ConvertOsmAddressesAsync(
        string inputPath, string outputPath, string country)
    {
        string? headerLine;
        using (var sr = new StreamReader(inputPath, detectEncodingFromByteOrderMarks: true))
        {
            headerLine = await sr.ReadLineAsync();
        }

        if (headerLine == null)
        {
            throw new InvalidDataException("File is empty.");
        }

        var headers = headerLine.Split('\t')
            .Select(h => h.Trim().ToLowerInvariant())
            .ToArray();

        int idxPostal = IndexOf(headers, "postal_code_");
        int idxCity   = IndexOf(headers, "city_");
        int idxXMin   = IndexOf(headers, "x_min");
        int idxXMax   = IndexOf(headers, "x_max");
        int idxYMin   = IndexOf(headers, "y_min");
        int idxYMax   = IndexOf(headers, "y_max");

        if (idxPostal < 0 || idxCity < 0)
        {
            throw new InvalidDataException("postal_code_ or city_ column not found.");
        }

        // Accumulate lat/lng averages per (postal_code, city) key
        var accum      = new Dictionary<string, LatLngAccum>(StringComparer.OrdinalIgnoreCase);
        var cityMap    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var defaultMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        int lineNum    = 1;
        int skipped    = 0;

        using (var sr = new StreamReader(inputPath, detectEncodingFromByteOrderMarks: true))
        {
            await sr.ReadLineAsync(); // skip header
            string? line;
            while ((line = await sr.ReadLineAsync()) != null)
            {
                lineNum++;
                if (string.IsNullOrWhiteSpace(line)) { continue; }

                var cols = line.Split('\t');

                var postalCode = SafeCol(cols, idxPostal);
                var city       = SafeCol(cols, idxCity);

                if (string.IsNullOrWhiteSpace(postalCode) || string.IsNullOrWhiteSpace(city))
                {
                    skipped++;
                    continue;
                }

                var key = $"{postalCode}|{city}";

                if (!defaultMap.ContainsKey(postalCode))
                {
                    defaultMap[postalCode] = true;
                }

                if (!cityMap.ContainsKey(key))
                {
                    cityMap[key] = city;
                    accum[key]   = new LatLngAccum();
                }

                if (idxXMin >= 0 && idxXMax >= 0 && idxYMin >= 0 && idxYMax >= 0)
                {
                    if (TryParseDouble(SafeCol(cols, idxXMin), out var xMin) &&
                        TryParseDouble(SafeCol(cols, idxXMax), out var xMax) &&
                        TryParseDouble(SafeCol(cols, idxYMin), out var yMin) &&
                        TryParseDouble(SafeCol(cols, idxYMax), out var yMax))
                    {
                        accum[key].Add((xMin + xMax) / 2.0, (yMin + yMax) / 2.0);
                    }
                }
            }
        }

        if (skipped > 0)
        {
            Console.WriteLine($"  ⚠ Skipped {skipped:N0} rows with missing postal_code or city.");
        }

        // Build output rows — first key per postal_code is is_default
        var seenDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputRows  = new List<CsvRow>();
        int rowNum      = 0;

        foreach (var (key, city) in cityMap)
        {
            rowNum++;
            var postalCode = key.Split('|')[0];
            var isDefault  = seenDefault.Add(postalCode);

            var a      = accum[key];
            var latStr = a.Count > 0
                ? (a.SumLat / a.Count).ToString("F4", CultureInfo.InvariantCulture)
                : "---";
            var lngStr = a.Count > 0
                ? (a.SumLng / a.Count).ToString("F4", CultureInfo.InvariantCulture)
                : "---";

            outputRows.Add(new CsvRow
            {
                RecordNumber = rowNum,
                ZpCode       = postalCode,
                PlaceName    = city,
                Timezone     = "---",
                IsDefault    = isDefault ? "true" : "false",
                Lat          = latStr,
                Lng          = lngStr,
                Admin1       = "---",
                Admin1Code   = "---",
            });
        }

        WriteCandidateCsv(outputRows, outputPath);
        return outputRows.Count;
    }

    // =========================================================================
    // OpenStreetMap Houses converter
    //
    // Input header (tab-delimited):
    //   postal_code, city, street, house_number, x, y, country
    //
    // lat = average y per (postal_code, city); lng = average x.
    // Yields one row per unique (postal_code, city).
    // =========================================================================

    private static async Task<int> ConvertOsmHousesAsync(
        string inputPath, string outputPath, string country)
    {
        string? headerLine;
        using (var sr = new StreamReader(inputPath, detectEncodingFromByteOrderMarks: true))
        {
            headerLine = await sr.ReadLineAsync();
        }

        if (headerLine == null)
        {
            throw new InvalidDataException("File is empty.");
        }

        var headers = headerLine.Split('\t')
            .Select(h => h.Trim().ToLowerInvariant())
            .ToArray();

        int idxPostal = IndexOf(headers, "postal_code");
        int idxCity   = IndexOf(headers, "city");
        int idxX      = IndexOf(headers, "x");
        int idxY      = IndexOf(headers, "y");

        if (idxPostal < 0 || idxCity < 0)
        {
            throw new InvalidDataException("postal_code or city column not found.");
        }

        var accum      = new Dictionary<string, LatLngAccum>(StringComparer.OrdinalIgnoreCase);
        var cityMap    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int lineNum    = 1;
        int skipped    = 0;

        using (var sr = new StreamReader(inputPath, detectEncodingFromByteOrderMarks: true))
        {
            await sr.ReadLineAsync(); // skip header
            string? line;
            while ((line = await sr.ReadLineAsync()) != null)
            {
                lineNum++;
                if (string.IsNullOrWhiteSpace(line)) { continue; }

                var cols       = line.Split('\t');
                var postalCode = SafeCol(cols, idxPostal);
                var city       = SafeCol(cols, idxCity);

                if (string.IsNullOrWhiteSpace(postalCode) || string.IsNullOrWhiteSpace(city))
                {
                    skipped++;
                    continue;
                }

                var key = $"{postalCode}|{city}";

                if (!cityMap.ContainsKey(key))
                {
                    cityMap[key] = city;
                    accum[key]   = new LatLngAccum();
                }

                if (idxX >= 0 && idxY >= 0)
                {
                    if (TryParseDouble(SafeCol(cols, idxX), out var x) &&
                        TryParseDouble(SafeCol(cols, idxY), out var y))
                    {
                        accum[key].Add(x, y);
                    }
                }
            }
        }

        if (skipped > 0)
        {
            Console.WriteLine($"  ⚠ Skipped {skipped:N0} rows with missing postal_code or city.");
        }

        var seenDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputRows  = new List<CsvRow>();
        int rowNum      = 0;

        foreach (var (key, city) in cityMap)
        {
            rowNum++;
            var postalCode = key.Split('|')[0];
            var isDefault  = seenDefault.Add(postalCode);

            var a      = accum[key];
            var latStr = a.Count > 0
                ? (a.SumLat / a.Count).ToString("F4", CultureInfo.InvariantCulture)
                : "---";
            var lngStr = a.Count > 0
                ? (a.SumLng / a.Count).ToString("F4", CultureInfo.InvariantCulture)
                : "---";

            outputRows.Add(new CsvRow
            {
                RecordNumber = rowNum,
                ZpCode       = postalCode,
                PlaceName    = city,
                Timezone     = "---",
                IsDefault    = isDefault ? "true" : "false",
                Lat          = latStr,
                Lng          = lngStr,
                Admin1       = "---",
                Admin1Code   = "---",
            });
        }

        WriteCandidateCsv(outputRows, outputPath);
        return outputRows.Count;
    }

    // =========================================================================
    // CSV output
    // =========================================================================

    private static void WriteCandidateCsv(IReadOnlyList<CsvRow> rows, string outputPath)
    {
        // Sort: zip ascending, default row first per zip, then city alphabetically.
        var sorted = rows
            .OrderBy(r  => r.ZpCode,    StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.IsDefault == "true")
            .ThenBy(r   => r.PlaceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CsvWriter.Write(sorted, outputPath);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static int IndexOf(string[] headers, string name)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            if (headers[i].Equals(name, StringComparison.OrdinalIgnoreCase)) { return i; }
        }
        return -1;
    }

    private static string SafeCol(string[] cols, int index) =>
        index >= 0 && index < cols.Length ? cols[index].Trim() : "";

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // =========================================================================
    // Arg parsing
    // =========================================================================


    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools convert <file.tsv> [--country XX] [--output path/to/output.csv] [--no-prompts]

              Converts a known third-party postal-code data file to candidate CSV format.

              Supported formats (auto-detected):
                GeoNames            — tab-delimited, 12 columns, no header
                OpenStreetMap streets   — tab or comma delimited, header row
                OpenStreetMap addresses — tab-delimited, header row (with lat/lng midpoints)
                OpenStreetMap houses    — tab-delimited, header row (with lat/lng averages)

              Output is written as {cc}-candidate.csv alongside the source file unless --output is given.
              Timezone is set to '---' for all rows; run 'ingest coords' to resolve after import.

              --country XX     Override country code (default: derived from filename prefix)
              --output path    Override output file path
              --no-prompts     Skip the format confirmation prompt
            """);

    // =========================================================================
    // Supporting types
    // =========================================================================

    /// <summary>Accumulates lat/lng values for averaging.</summary>
    private sealed class LatLngAccum
    {
        public double SumLat { get; private set; }
        public double SumLng { get; private set; }
        public int    Count  { get; private set; }

        public void Add(double lng, double lat)
        {
            SumLng += lng;
            SumLat += lat;
            Count++;
        }
    }
}