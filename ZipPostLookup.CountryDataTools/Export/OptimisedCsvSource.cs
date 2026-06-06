namespace ZipPostLookup.CountryDataTools.Export;

/// <summary>
/// Reads an <b>optimised</b> ZipPostLookup CSV — the build artifact produced by
/// <c>export --target main</c> for a pipeline country and embedded at
/// <c>ZipPostLookup/Data/{cc}/{cc}.csv</c> — back into <see cref="ExportRow"/>s plus the
/// <see cref="ExportMeta"/> reconstructed from its <c>#meta:</c> header.
///
/// <para>This is the inverse of <see cref="CsvExporter"/>. It exists so a ZP frozen image can be
/// regenerated from the committed CSV <b>with no working database</b>: the CSV is the exact data
/// the runtime <c>ZipPostRegistry</c> loads, so an image built from it is parity-guaranteed against
/// the registry (see <c>ZpImageParityTests</c>). Use it when the image format changes but the
/// underlying data has not — rebuilding from the DB is unnecessary and Docker may be unavailable.</para>
///
/// <para>The <c>#meta:</c> timezone/admin1 index tables are decoded back to strings; range rows
/// (a code containing <c>':'</c>, e.g. <c>"A1A0**:A1A6**"</c>) are preserved verbatim so the image's
/// range table mirrors the CSV. The decoded index tables are returned in <see cref="ExportMeta"/> so
/// the image reuses the identical tz/admin ordering the CSV used.</para>
/// </summary>
internal static class OptimisedCsvSource
{
    public static (List<ExportRow> Rows, ExportMeta Meta) Read(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);

        var line = reader.ReadLine();

        string[]?                       tzTable    = null;
        (string Code, string Name)[]?   adminTable = null;
        string[]?                       levelNames = null;

        if (line != null && line.StartsWith("#meta:", StringComparison.Ordinal))
        {
            ParseMeta(line, out tzTable, out adminTable, out levelNames);
            line = reader.ReadLine(); // advance to the column header
        }

        if (line == null)
        {
            throw new InvalidDataException($"'{path}' has no column header row.");
        }

        var columns       = BuildColumnMap(SplitCsv(line));
        var includeCoords = columns.ContainsKey("lat") && columns.ContainsKey("lng");

        var rows = new List<ExportRow>();
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) { continue; }

            var fields = SplitCsv(line);

            var row = new ExportRow
            {
                ZpCode    = Field(fields, columns, "code"),
                PlaceName = Field(fields, columns, "name"),
                IsDefault = ParseBool(Field(fields, columns, "isdefault")),
                Timezone  = DecodeTimezone(Field(fields, columns, "timezone"), tzTable),
            };

            DecodeAdmin(
                Field(fields, columns, "admin1code"),
                Field(fields, columns, "admin1"),
                adminTable,
                out var adminName,
                out var adminCode);
            row.Admin1     = adminName;
            row.Admin1Code = adminCode;

            if (includeCoords)
            {
                row.Lat = Field(fields, columns, "lat");
                row.Lng = Field(fields, columns, "lng");
            }

            rows.Add(row);
        }

        var meta = new ExportMeta
        {
            IncludeCoords    = includeCoords,
            TimezoneIndex    = tzTable,
            AdminIndex       = adminTable,
            AdminLevelNames  = levelNames,
        };

        return (rows, meta);
    }

    // -------------------------------------------------------------------------
    // #meta: header  (mirrors CsvExporter.BuildMetaLine)
    // -------------------------------------------------------------------------

    private static void ParseMeta(
        string line,
        out string[]? tzTable,
        out (string Code, string Name)[]? adminTable,
        out string[]? levelNames)
    {
        tzTable    = null;
        adminTable = null;
        levelNames = null;

        var body = line.Substring("#meta:".Length);
        foreach (var segment in body.Split(';'))
        {
            if (segment.StartsWith("tz=", StringComparison.Ordinal))
            {
                tzTable = segment.Substring(3).Split('|');
            }
            else if (segment.StartsWith("admin=", StringComparison.Ordinal))
            {
                adminTable = Array.ConvertAll(
                    segment.Substring(6).Split('|'),
                    entry =>
                    {
                        var colon = entry.IndexOf(':');
                        return colon < 0
                            ? (Code: entry, Name: entry)
                            : (Code: entry[..colon], Name: entry[(colon + 1)..]);
                    });
            }
            else if (segment.StartsWith("levels=", StringComparison.Ordinal))
            {
                levelNames = segment.Substring(7).Split('|');
            }
        }
    }

    // -------------------------------------------------------------------------
    // Field decoding
    // -------------------------------------------------------------------------

    private static string DecodeTimezone(string raw, string[]? table) =>
        table != null && int.TryParse(raw, out var idx) && idx >= 0 && idx < table.Length
            ? table[idx]
            : raw;

    private static void DecodeAdmin(
        string codeColumn,
        string nameColumn,
        (string Code, string Name)[]? table,
        out string name,
        out string code)
    {
        // In the optimised CSV both the Admin1 and Admin1Code columns carry the same integer index.
        if (table != null && int.TryParse(codeColumn, out var idx) && idx >= 0 && idx < table.Length)
        {
            name = table[idx].Name;
            code = table[idx].Code;
        }
        else
        {
            name = nameColumn;
            code = codeColumn;
        }
    }

    private static bool ParseBool(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static string Field(string[] fields, Dictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var i) && i < fields.Length ? fields[i] : "";

    private static Dictionary<string, int> BuildColumnMap(string[] header)
    {
        var map = new Dictionary<string, int>(header.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            map[header[i].Trim()] = i;
        }

        return map;
    }

    // -------------------------------------------------------------------------
    // RFC 4180 field splitting (quoted strings, "" escapes; ints/bools unquoted)
    // -------------------------------------------------------------------------

    private static string[] SplitCsv(string line)
    {
        var fields    = new List<string>(8);
        var sb        = new System.Text.StringBuilder();
        var inQuotes  = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else { inQuotes = false; }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString());
        return fields.ToArray();
    }
}
