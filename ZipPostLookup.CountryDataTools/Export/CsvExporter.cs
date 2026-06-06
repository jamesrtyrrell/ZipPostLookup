using System.Text;

namespace ZipPostLookup.CountryDataTools.Export;

/// <summary>
/// Writes a list of <see cref="ExportRow"/> objects to a UTF-8-with-BOM CSV file.
///
/// Output format depends on <see cref="ExportMeta"/>:
///
///   Standard (no optimisation):
///     Code,Name,Timezone,IsDefault,Lat,Lng,Admin1,Admin1Code
///     "10001","New York","America/New_York",true,"40.75","-73.99","New York","NY"
///
///   Optimised (indexed + lat/lng stripped):
///     #meta:tz=America/Edmonton|America/Toronto|...;admin=AB:Alberta|ON:Ontario|...
///     Code,Name,Timezone,IsDefault,Admin1,Admin1Code
///     "T0A0**:T0A9**","Abee",0,true,0,0
///
/// The <c>#meta:</c> line is parsed by <c>BuiltInDataSource</c> to rebuild the
/// timezone and admin1 lookup tables before reading data rows.
///
/// Integer indices are written unquoted (like <c>IsDefault</c>).
/// All string fields are always double-quoted per RFC 4180.
/// </summary>
internal static class CsvExporter
{
    public static async Task WriteAsync(
        IReadOnlyList<ExportRow> rows,
        ExportMeta meta,
        string outputPath)
    {
        // Pre-build reverse-lookup dictionaries for index substitution
        var tzLookup    = BuildTzLookup(meta.TimezoneIndex);
        var adminLookup = BuildAdminLookup(meta.AdminIndex);

        await using var writer = new StreamWriter(
            outputPath,
            append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // ── #meta: header (only when indexes are present) ────────────────────
        if (meta.IsIndexed)
        {
            await writer.WriteLineAsync(BuildMetaLine(meta));
        }

        // ── Column header ─────────────────────────────────────────────────────
        if (meta.IncludeCoords)
        {
            await writer.WriteLineAsync("Code,Name,Timezone,IsDefault,Lat,Lng,Admin1,Admin1Code");
        }
        else
        {
            await writer.WriteLineAsync("Code,Name,Timezone,IsDefault,Admin1,Admin1Code");
        }

        // ── Data rows ─────────────────────────────────────────────────────────
        foreach (var row in rows)
        {
            await writer.WriteLineAsync(BuildDataLine(row, meta, tzLookup, adminLookup));
        }
    }

    // -------------------------------------------------------------------------
    // Line builders
    // -------------------------------------------------------------------------

    private static string BuildMetaLine(ExportMeta meta)
    {
        var sb = new StringBuilder("#meta:");

        if (meta.TimezoneIndex != null)
        {
            sb.Append("tz=");
            sb.Append(string.Join("|", meta.TimezoneIndex));
        }

        if (meta.AdminIndex != null)
        {
            if (meta.TimezoneIndex != null) { sb.Append(';'); }
            sb.Append("admin=");
            sb.Append(string.Join("|", Array.ConvertAll(
                meta.AdminIndex,
                a => $"{a.Code}:{a.Name}")));
        }

        if (meta.AdminLevelNames is { Length: > 0 })
        {
            sb.Append(";levels=");
            sb.Append(string.Join("|", meta.AdminLevelNames));
        }

        return sb.ToString();
    }

    private static string BuildDataLine(
        ExportRow row,
        ExportMeta meta,
        Dictionary<string, int>? tzLookup,
        Dictionary<string, int>? adminLookup)
    {
        var code      = Quote(row.ZpCode);
        var name      = Quote(row.PlaceName);
        var isDefault = row.IsDefault ? "true" : "false";

        string timezone;
        string admin1;
        string admin1Code;

        if (tzLookup != null && tzLookup.TryGetValue(row.Timezone, out var tzIdx))
        {
            timezone = tzIdx.ToString();
        }
        else
        {
            timezone = Quote(row.Timezone);
        }

        if (adminLookup != null && adminLookup.TryGetValue(row.Admin1Code, out var adminIdx))
        {
            // Both Admin1 and Admin1Code columns carry the same integer index —
            // BuiltInDataSource resolves name and code from the same lookup entry.
            admin1     = adminIdx.ToString();
            admin1Code = adminIdx.ToString();
        }
        else
        {
            admin1     = Quote(row.Admin1);
            admin1Code = Quote(row.Admin1Code);
        }

        if (meta.IncludeCoords)
        {
            var lat = Quote(row.Lat);
            var lng = Quote(row.Lng);
            return $"{code},{name},{timezone},{isDefault},{lat},{lng},{admin1},{admin1Code}";
        }

        return $"{code},{name},{timezone},{isDefault},{admin1},{admin1Code}";
    }

    // -------------------------------------------------------------------------
    // Lookup table builders
    // -------------------------------------------------------------------------

    private static Dictionary<string, int>? BuildTzLookup(string[]? index)
    {
        if (index == null) { return null; }

        var dict = new Dictionary<string, int>(
            index.Length, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < index.Length; i++)
        {
            dict[index[i]] = i;
        }

        return dict;
    }

    private static Dictionary<string, int>? BuildAdminLookup(
        (string Code, string Name)[]? index)
    {
        if (index == null) { return null; }

        var dict = new Dictionary<string, int>(
            index.Length, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < index.Length; i++)
        {
            dict[index[i].Code] = i;
        }

        return dict;
    }

    // -------------------------------------------------------------------------
    // CSV helpers
    // -------------------------------------------------------------------------

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";
}