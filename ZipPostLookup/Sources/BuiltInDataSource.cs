using System.Reflection;
using System.Text.Json;
using ZipPostLookup.Core;

namespace ZipPostLookup.Sources;

/// <summary>
/// Loads postal entry data from an embedded <c>Data/{cc}/{cc}.csv.br</c> resource (net8+)
/// or <c>Data/{cc}/{cc}.csv</c> (netstandard2.0 / Legacy package).
/// The net8+ build embeds Brotli-compressed CSVs to reduce package size; the stream is
/// decompressed transparently before parsing.
///
/// Supported CSV formats:
///
///   Standard 6-column (no lat/lng — produced by CountryDataTools --target main):
///     Code,Name,Timezone,IsDefault,Admin1,Admin1Code
///     "10001","New York","America/New_York",true,"New York","NY"
///
///   Legacy 8-column (includes lat/lng):
///     code,name,timezone,is_default,lat,lng,Admin1,Admin1Code
///     "10001","New York","America/New_York",true,"40.75","-73.99","New York","NY"
///
///   Optimised 6-column indexed (produced by US/CA/MX export pipeline):
///     #meta:tz=America/Edmonton|America/Toronto|...;admin=AB:Alberta|ON:Ontario|...
///     code,name,timezone,is_default,admin1,Admin1Code
///     "T0A0**:T0A9**","Abee",0,true,0,0
///
/// Detection order:
///   1. If first line starts with "#meta:" → optimised indexed format.
///   2. If 8+ columns per row              → legacy format (lat/lng at [4],[5]).
///   3. If 6 columns per row               → standard format (no lat/lng).
///
/// lat/lng always return <c>null</c> for formats 1 and 3.
///
/// Rules (all formats):
///   · All text fields are always double-quoted
///   · Missing values use "---" (not null, not empty)
///   · is_default is unquoted true/false
///   · Integer indices (optimised format) are unquoted
///   · CA uses range notation in the code column: "B2G0**:B2G9**"
///     Range entries are yielded with the range code intact — ZipRegistry handles expansion.
///
/// Adding a new country: embed <c>Data/{cc}/{cc}.csv.br</c> (net8+ package) and
/// <c>Data/{cc}/{cc}.csv</c> (Legacy package), both marked as <c>EmbeddedResource</c>.
/// </summary>
internal sealed class BuiltInDataSource : ICodeDataSource
{
    private readonly CountryCode _country;

    public string SourceName => $"built-in-{(string)_country}";

    public BuiltInDataSource(CountryCode country)
    {
        _country = country;
    }

    public IEnumerable<CodeEntry> GetEntries()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var cc       = ((string)_country).ToLowerInvariant();
#if NET8_0_OR_GREATER
        var fileName = $"{cc}.csv.br";
#else
        var fileName = $"{cc}.csv";
#endif

        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException(
                $"No built-in data exists for country '{_country}'. " +
                $"To add support, embed a CSV file at Data/{cc}/{cc}.csv " +
                $"and mark it as EmbeddedResource in the .csproj, " +
                $"or implement ICodeDataSource to supply your own data.");

        using var rawStream = assembly.GetManifestResourceStream(resourceName)!;
#if NET8_0_OR_GREATER
        using var decompressedStream = new System.IO.Compression.BrotliStream(
            rawStream, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(decompressedStream);
#else
        using var reader = new StreamReader(rawStream);
#endif

        // ── Detect optimised format via optional #meta: first line ────────────
        string[]? tzIndex    = null;
        (string Code, string Name)[]? adminIndex = null;
        string[]? levelNames = null;

        var firstLine = reader.ReadLine();

        if (firstLine != null &&
            firstLine.StartsWith("#meta:", StringComparison.Ordinal))
        {
            ParseMetaLine(firstLine, out tzIndex, out adminIndex, out levelNames);
            reader.ReadLine(); // consume the column header line
        }
        // else: firstLine was the column header — already consumed, nothing more to do

        // Level names: from #meta:levels= when present; fall back to CountryInfoSource.
        levelNames ??= CountryInfoSource.GetAdminLevelNames(cc).ToArray();

        // ── Read data rows ────────────────────────────────────────────────────
        string? line;
        var lineNumber = 2; // 1 = header (or meta), already consumed

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
                entry = ParseLine(line, levelNames, tzIndex, adminIndex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse {fileName} at line {lineNumber}: '{line}'", ex);
            }

            if (entry != null)
            {
                yield return entry;
            }
        }
    }

    // =========================================================================
    // #meta: line parsing
    // =========================================================================

    /// <summary>
    /// Parses the <c>#meta:</c> header line into timezone and admin1 lookup tables.
    ///
    /// Format:
    ///   #meta:tz=America/Toronto|America/Vancouver|...;admin=ON:Ontario|QC:Quebec|...
    ///
    /// Separators:
    ///   ;  separates the tz and admin sections
    ///   |  separates values within each section
    ///   :  separates the admin code from the admin name (first occurrence only)
    /// </summary>
    private static void ParseMetaLine(
        string line,
        out string[]? tzIndex,
        out (string Code, string Name)[]? adminIndex,
        out string[]? levelNames)
    {
        tzIndex    = null;
        adminIndex = null;
        levelNames = null;

        var content = line.Substring(6); // strip "#meta:"
        var parts   = content.Split(';');

        foreach (var part in parts)
        {
            if (part.StartsWith("tz=", StringComparison.Ordinal))
            {
                tzIndex = part.Substring(3).Split('|');
            }
            else if (part.StartsWith("admin=", StringComparison.Ordinal))
            {
                var entries   = part.Substring(6).Split('|');
                var adminList = new List<(string, string)>(entries.Length);
                foreach (var entry in entries)
                {
                    var colonIdx = entry.IndexOf(':');
                    if (colonIdx > 0)
                        adminList.Add((entry.Substring(0, colonIdx), entry.Substring(colonIdx + 1)));
                }
                adminIndex = adminList.ToArray();
            }
            else if (part.StartsWith("levels=", StringComparison.Ordinal))
            {
                levelNames = part.Substring(7).Split('|');
            }
        }
    }

    // =========================================================================
    // CSV row parsing
    // =========================================================================

    private static CodeEntry? ParseLine(
        string line,
        string[] levelNames,
        string[]? tzIndex,
        (string Code, string Name)[]? adminIndex)
    {
        var fields = SplitLine(line);

        // ── Optimised 6-column indexed format ─────────────────────────────────
        if (tzIndex != null)
        {
            return ParseIndexedLine(fields, levelNames, tzIndex, adminIndex);
        }

        // ── Standard 8-column format ──────────────────────────────────────────
        return ParseStandardLine(fields, levelNames);
    }

    /// <summary>
    /// Parses an optimised row: 6 columns, integer indices for tz and admin1.
    ///   fields[0] = code  (range or exact, quoted)
    ///   fields[1] = name  (quoted)
    ///   fields[2] = tz index (unquoted integer)
    ///   fields[3] = is_default (unquoted true/false)
    ///   fields[4] = admin1 index (unquoted integer, resolves to admin1 name)
    ///   fields[5] = Admin1Code index (same integer, resolves to admin1 code)
    /// </summary>
    private static CodeEntry? ParseIndexedLine(
        List<string> fields,
        string[] levelNames,
        string[] tzIndex,
        (string Code, string Name)[]? adminIndex)
    {
        if (fields.Count < 6)
        {
            return null;
        }

        var code = Unquote(fields[0]);
        var name = Unquote(fields[1]);

        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        // Resolve timezone from index
        var tzIdxRaw = Unquote(fields[2]);
        if (!int.TryParse(tzIdxRaw, out var tzIdx) ||
            tzIdx < 0 || tzIdx >= tzIndex.Length)
        {
            return null;
        }

        var timezone  = tzIndex[tzIdx];
        var isDefault = string.Equals(
            fields[3].Trim(), "true", StringComparison.OrdinalIgnoreCase);

        // Resolve admin1 from index
        var admin1     = "---";
        var admin1Code = "---";

        if (adminIndex != null)
        {
            var adminIdxRaw = Unquote(fields[4]);
            if (int.TryParse(adminIdxRaw, out var adminIdx) &&
                adminIdx >= 0 && adminIdx < adminIndex.Length)
            {
                admin1Code = adminIndex[adminIdx].Code;
                admin1     = adminIndex[adminIdx].Name;
            }
        }

        var levelName = levelNames.Length > 0 ? levelNames[0] : "Admin1";
        var admins    = admin1 != "---"
            ? new[] { new AdminLevel(admin1, admin1Code, levelName) }
            : Array.Empty<AdminLevel>();

        // Optional reason field at index 6 (unquoted integer; absent in older CSVs → None).
        var reason = CodeReason.None;
        if (fields.Count >= 7 && int.TryParse(fields[6].Trim(), out var r))
            reason = (CodeReason)r;

        return new CodeEntry(
            ZpCode:    code,
            PlaceName: name,
            Timezone:  timezone,
            IsDefault: isDefault,
            Admins:    admins,
            Reason:    reason);
    }

    /// <summary>
    /// Parses a standard row: either 6-column (no lat/lng) or legacy 8-column (lat/lng included).
    ///
    ///   6-column: code, name, timezone, is_default, admin1_value, admin1_code [, more admin pairs]
    ///   8-column: code, name, timezone, is_default, lat, lng, admin1_value, admin1_code [, ...]
    ///
    /// Column count drives the branch — 8+ columns means lat/lng are present at [4] and [5],
    /// admin pairs start at [6].  6 or 7 columns means no lat/lng, admin pairs start at [4].
    /// </summary>
    private static CodeEntry? ParseStandardLine(
        List<string> fields, string[] levelNames)
    {
        if (fields.Count < 6)
        {
            return null;
        }

        var code      = Unquote(fields[0]);
        var name      = Unquote(fields[1]);
        var timezone  = Unquote(fields[2]);
        var isDefault = string.Equals(
            fields[3].Trim(), "true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(timezone))
        {
            return null;
        }

        // Legacy 8-column format had lat/lng at [4] and [5]; skip them.
        var adminStart = fields.Count >= 8 ? 6 : 4;

        // Dynamic admin columns — pairs of (value, code) starting at adminStart
        var adminList  = new List<AdminLevel>();
        var levelIndex = 0;

        while (adminStart + 1 < fields.Count)
        {
            var adminValue = Unquote(fields[adminStart]);
            var adminCode  = Unquote(fields[adminStart + 1]);
            var levelName  = levelIndex < levelNames.Length
                ? levelNames[levelIndex]
                : $"Admin{levelIndex + 1}";

            adminList.Add(new AdminLevel(adminValue, adminCode, levelName));

            adminStart += 2;
            levelIndex++;
        }

        // Optional reason field — the single leftover column after all admin pairs.
        var reason = CodeReason.None;
        if (adminStart < fields.Count && int.TryParse(Unquote(fields[adminStart]), out var r))
            reason = (CodeReason)r;

        return new CodeEntry(
            ZpCode:    code,
            PlaceName: name,
            Timezone:  timezone,
            IsDefault: isDefault,
            Admins:    adminList.ToArray(),
            Reason:    reason);
    }

    // =========================================================================
    // Info JSON loading — reads {cc}_info.json for admin level names
    // =========================================================================

    // =========================================================================
    // CSV helpers
    // =========================================================================

    /// <summary>
    /// Splits a CSV line respecting double-quoted fields that may contain commas.
    /// </summary>
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

                if (pos < line.Length && line[pos] == ',')
                {
                    pos++;
                }
            }
            else
            {
                var comma = line.IndexOf(',', pos);

                if (comma < 0)
                {
                    fields.Add(line.Substring(pos));
                    break;
                }

                fields.Add(line.Substring(pos, comma - pos));
                pos = comma + 1;
            }
        }

        return fields;
    }

    /// <summary>
    /// Removes surrounding double-quotes from a field value and trims whitespace.
    /// Returns the raw trimmed value if not quoted.
    /// </summary>
    private static string Unquote(string field)
    {
        var trimmed = field.Trim();

        if (trimmed.Length >= 2 &&
            trimmed[0] == '"' &&
            trimmed[trimmed.Length - 1] == '"')
        {
            return trimmed.Substring(1, trimmed.Length - 2).Replace("\"\"", "\"");
        }

        return trimmed;
    }
}