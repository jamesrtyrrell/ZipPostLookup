using System.Reflection;
using System.Text.Json;
using ZipPostLookup.Core;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Json;

namespace ZipPostLookup.CountryDataTools.DSV;

/// <summary>
/// Loads <see cref="CodeEntry"/> records for a country's source-of-truth reference data.
///
/// <para>The reference CSVs are large (CA ~120&#160;MB raw) so they are <b>not</b> embedded in the
/// assembly; they live on disk under <c>ZipPostLookup.CountryDataTools/Data/{cc}/</c>, stored
/// gzip-compressed as <c>{cc}.csv.tar.gz</c> (with a plain <c>{cc}.csv</c> fallback). This loader
/// reads that file from the repo (CountryDataTools always runs from the repo root) and expands it
/// in-memory — the <c>tar -xvzf</c> equivalent (see <see cref="TarGzArchive"/>). The small admin
/// schema (<c>{cc}_info.json</c>) is still read from the embedded resource.</para>
///
/// CSV format (ReferenceDataCSV — PascalCase headers):
///   Code,Name,Timezone,IsDefault,Lat,Lng,Admin1,Admin1Code,TimezoneChecked,NameChecked
///   "T0A 1A0","Abee","America/Edmonton",true,"54.23","-113.45","Alberta","AB",true,true
///
/// Column discovery is header-based so new columns (TimezoneChecked, NameChecked,
/// Admin2, Admin2Code, ...) are handled correctly without positional assumptions.
/// </summary>
internal static class EmbeddedCsvLoader
{
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    /// <summary>On-disk reference data directory for <paramref name="cc"/> (repo-relative).</summary>
    private static string DataDir(string cc) =>
        Path.Combine(Directory.GetCurrentDirectory(), "ZipPostLookup.CountryDataTools", "Data", cc);

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if a reference CSV exists on disk for <paramref name="countryCode"/> —
    /// either the compressed <c>{cc}.csv.tar.gz</c> (preferred) or a plain <c>{cc}.csv</c>.
    /// </summary>
    public static bool Exists(string countryCode)
    {
        var cc = countryCode.ToLowerInvariant();
        var dir = DataDir(cc);
        return File.Exists(Path.Combine(dir, $"{cc}{TarGzArchive.Suffix}")) ||
               File.Exists(Path.Combine(dir, $"{cc}.csv"));
    }

    /// <summary>
    /// Loads all <see cref="CodeEntry"/> records from the embedded CSV
    /// for <paramref name="countryCode"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no embedded CSV is found for the given country code.
    /// </exception>
    public static IReadOnlyList<CodeEntry> Load(string countryCode)
    {
        var cc = countryCode.ToLowerInvariant();
        var fileName = $"{cc}.csv";

        var levelNames = LoadAdminLevelNames(cc);

        using var stream = OpenReferenceCsvStream(cc);
        using var reader = new StreamReader(stream);

        // Read and parse the header to build a column → index map.
        // All subsequent row parsing is header-driven, not positional, so new
        // columns (TimezoneChecked, NameChecked, Admin2, ...) are handled safely.
        var headerLine = reader.ReadLine();
        if (headerLine == null)
        {
            return [];
        }

        var columnIndex = ParseHeader(headerLine);

        var entries = new List<CodeEntry>();
        string? line;
        int lineNumber = 1;

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            CodeEntry? entry;

            try
            {
                entry = ParseLine(line, columnIndex, levelNames);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse {fileName} at line {lineNumber}: '{line}'", ex);
            }

            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    // -------------------------------------------------------------------------
    // Embedded resource resolution (compressed .csv.tar.gz preferred, plain .csv fallback)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens a readable stream over the reference CSV text for <paramref name="cc"/>, read from disk.
    /// Prefers the compressed <c>{cc}.csv.tar.gz</c> (expanded in-memory — the <c>tar -xvzf</c>
    /// equivalent), falling back to a plain <c>{cc}.csv</c>.
    /// </summary>
    private static Stream OpenReferenceCsvStream(string cc)
    {
        var dir = DataDir(cc);

        var archivePath = Path.Combine(dir, $"{cc}{TarGzArchive.Suffix}");
        if (File.Exists(archivePath))
        {
            using var compressed = File.OpenRead(archivePath);
            return new MemoryStream(TarGzArchive.ExtractSingleCsv(compressed), writable: false);
        }

        var csvPath = Path.Combine(dir, $"{cc}.csv");
        if (File.Exists(csvPath))
        {
            return File.OpenRead(csvPath);
        }

        throw new InvalidOperationException(
            $"No reference CSV found for country '{cc}'. Expected " +
            $"{archivePath} (or {csvPath}). Run 'export --country {cc.ToUpperInvariant()} --target ref'.");
    }

    // -------------------------------------------------------------------------
    // Header parsing — builds a column-name → field-index map
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses the CSV header line into a case-insensitive column name → zero-based
    /// field index dictionary. Handles quoted column names.
    /// </summary>
    private static Dictionary<string, int> ParseHeader(string headerLine)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fields = SplitLine(headerLine);

        for (var i = 0; i < fields.Count; i++)
        {
            var colName = Unquote(fields[i]).Trim();
            if (!string.IsNullOrEmpty(colName))
            {
                map[colName] = i;
            }
        }

        return map;
    }

    // -------------------------------------------------------------------------
    // Admin level names — read from {cc}_info.json embedded in this assembly
    // -------------------------------------------------------------------------

    private static string[] LoadAdminLevelNames(string cc)
    {
        var infoName = _assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"{cc}_info.json", StringComparison.OrdinalIgnoreCase));

        if (infoName == null)
        {
            return Array.Empty<string>();
        }

        try
        {

            using var stream = _assembly.GetManifestResourceStream(infoName)!;
            using var doc = JsonDocument.Parse(stream);

            // use the ZipPostLookup.CountryDataTools/Models/Json/CountryInfoJson.cs to serialise the json
            var countryInfoJson = doc.Deserialize<CountryInfoJson>();
            if (countryInfoJson == null)
            {
                return Array.Empty<string>();
            }


            if (countryInfoJson.AdminLevels.Any())
            {
                return countryInfoJson.AdminLevels.Select(x => x.Name).ToArray();
            }



            return Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
        
    }

    // -------------------------------------------------------------------------
    // Admin level names — read from {cc}_info.json embedded in this assembly
    // -------------------------------------------------------------------------

    public static List<DataAdminLevel> LoadAdminLevels(string cc)
    {
        var dataAdminLevels = new List<DataAdminLevel>();
        {
            var infoName = _assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"{cc}_info.json", StringComparison.OrdinalIgnoreCase));
     
            if (infoName == null) { return dataAdminLevels; }
     
            try
            {
     
                using var stream = _assembly.GetManifestResourceStream(infoName)!;
                using var doc    = JsonDocument.Parse(stream);
     
                // use the ZipPostLookup.CountryDataTools/Models/Json/CountryInfoJson.cs to serialise the json
                var countryInfoJson = doc.Deserialize<CountryInfoJson>();
                if(countryInfoJson == null)
                {
                    return dataAdminLevels;
                }
                 
                if (countryInfoJson.AdminLevels.Any())
                {
                    foreach (var adminLevel in countryInfoJson.AdminLevels)
                    {
                        var dataAdminLevel = new DataAdminLevel(adminLevel, cc);
                        dataAdminLevels.Add(dataAdminLevel);
                    }
                
                }
                return dataAdminLevels;
            }
            catch
            {
                return dataAdminLevels;
            }
        }
    }

    // -------------------------------------------------------------------------
    // CSV row parsing — header-driven, not positional
    // -------------------------------------------------------------------------

    private static CodeEntry? ParseLine(
        string line,
        Dictionary<string, int> col,
        string[] levelNames)
    {
        var fields = SplitLine(line);

        // Helper: safely read a field by column name; returns "" if absent.
        string Field(string name) =>
            col.TryGetValue(name, out var idx) && idx < fields.Count
                ? Unquote(fields[idx])
                : "";

        var code     = Field("Code");
        var name     = Field("Name");
        var timezone = Field("Timezone");

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(timezone))
        {
            return null;
        }

        var isDefault = string.Equals(Field("IsDefault"), "true", StringComparison.OrdinalIgnoreCase);

        // Admin levels — discover all Admin{N} / Admin{N}Code pairs present in the header.
        // Non-admin columns that follow (TimezoneChecked, NameChecked, etc.) are safely
        // ignored because we look up by name, not by sequential position.
        var adminList  = new List<AdminLevel>();
        var levelIndex = 0;

        for (var n = 1; ; n++)
        {
            var adminValueKey = n == 1 ? "Admin1"     : $"Admin{n}";
            var adminCodeKey  = n == 1 ? "Admin1Code" : $"Admin{n}Code";

            if (!col.ContainsKey(adminValueKey) || !col.ContainsKey(adminCodeKey)) { break; }

            var adminValue = Field(adminValueKey);
            var adminCode  = Field(adminCodeKey);
            var levelName  = levelIndex < levelNames.Length
                ? levelNames[levelIndex]
                : $"Admin{n}";

            adminList.Add(new AdminLevel(adminValue, adminCode, levelName));
            levelIndex++;
        }

        return new CodeEntry(
            ZpCode:    code,
            PlaceName: name,
            Timezone:  timezone,
            IsDefault: isDefault,
            Admins:    adminList.ToArray());
    }

    // -------------------------------------------------------------------------
    // CSV helpers
    // -------------------------------------------------------------------------

    private static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var pos    = 0;

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
                var comma = line.IndexOf(',', pos);
                if (comma < 0)
                {
                    fields.Add(line[pos..]);
                    break;
                }

                fields.Add(line.Substring(pos, comma - pos));
                pos = comma + 1;
            }
        }

        return fields;
    }

    private static string Unquote(string field)
    {
        var trimmed = field.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\"\"", "\"");
        }

        return trimmed;
    }

    public static List<CountriesJson>? GetCountriesInfo()
    {
        var infoName = _assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"Countries.json", StringComparison.OrdinalIgnoreCase));
     
        if (infoName == null) { return null; }
     
        try
        {
     
            using var stream = _assembly.GetManifestResourceStream(infoName)!;
            using var doc    = JsonDocument.Parse(stream);
            // use the ZipPostLookup.CountryDataTools/Models/Json/CountriesJson.cs to serialise the json
            return doc.Deserialize<List<CountriesJson>>();
        }
        catch
        {
            return null;
        }
    }
}