using System.Text;
using System.Text.Json;

namespace ZipPostLookup.CountryDataTools.Ingestion.AutoImport;

/// <summary>
/// Flattens a JSON file (array-of-objects or wrapped array) to a tabular CSV
/// so the rest of the auto-import pipeline is format-agnostic.
///
/// Supported shapes:
///   • Top-level array:        [{"zip":"10001","city":"New York"}, ...]
///   • Wrapped array (one key): {"data":[...]} — picks the first array-valued key
///   • Nested objects:          leaf scalar values are dot-path prefixed (e.g. "admin.code")
/// </summary>
public static class JsonFlattenerService
{
    /// <summary>
    /// Flatten the JSON at <paramref name="filePath"/> to a temp CSV and return its path.
    /// Caller is responsible for deleting the temp file.
    /// </summary>
    public static string ConvertToCsv(string filePath, int? maxRows = null)
    {
        var rows = ReadRows(filePath, maxRows);
        var tempPath = Path.Combine(Path.GetTempPath(), $"zpl-json-{Guid.NewGuid():N}.csv");
        WriteCsv(tempPath, rows);
        return tempPath;
    }

    /// <summary>
    /// Parse the JSON and return header + data rows as string arrays.
    /// Row 0 is always the header (field names).
    /// </summary>
    public static List<string[]> ReadRows(string filePath, int? maxRows = null)
    {
        using var stream = File.OpenRead(filePath);
        using var doc = JsonDocument.Parse(stream);

        var root = doc.RootElement;

        // Locate the array to iterate
        JsonElement arrayElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            arrayElement = root;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            // Pick first property whose value is an array
            JsonElement? found = null;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    found = prop.Value;
                    break;
                }
            }
            if (found == null)
                throw new InvalidOperationException(
                    "JSON root object has no array-valued property. " +
                    "Expected [{...},...] or {\"key\":[{...},...]}.");
            arrayElement = found.Value;
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported JSON root kind: {root.ValueKind}. Expected Array or Object.");
        }

        if (arrayElement.GetArrayLength() == 0)
            return new List<string[]>();

        // Collect all keys from a forward scan of up to 50 objects (union of all keys)
        var allKeys = new List<string>();
        var keySet  = new HashSet<string>(StringComparer.Ordinal);
        int scanLimit = Math.Min(50, arrayElement.GetArrayLength());
        int scanned = 0;
        foreach (var element in arrayElement.EnumerateArray())
        {
            if (scanned++ >= scanLimit) break;
            if (element.ValueKind != JsonValueKind.Object) continue;
            foreach (var kv in FlattenObject(element))
            {
                if (keySet.Add(kv.Key))
                    allKeys.Add(kv.Key);
            }
        }

        if (allKeys.Count == 0)
            return new List<string[]>();

        // Build rows
        var result = new List<string[]>();
        result.Add(allKeys.ToArray()); // header row

        int limit = maxRows ?? int.MaxValue;
        foreach (var element in arrayElement.EnumerateArray())
        {
            if (result.Count - 1 >= limit) break; // -1 for header
            if (element.ValueKind != JsonValueKind.Object) continue;

            var flat = FlattenObject(element);
            var row = allKeys.Select(k => flat.TryGetValue(k, out var v) ? v : "").ToArray();
            result.Add(row);
        }

        return result;
    }

    /// <summary>
    /// Recursively flatten an object element to dot-path key → string value pairs.
    /// Arrays of scalars are joined with "|"; nested object arrays are skipped.
    /// </summary>
    private static Dictionary<string, string> FlattenObject(JsonElement obj, string prefix = "")
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var prop in obj.EnumerateObject())
        {
            var key = prefix.Length > 0 ? $"{prefix}.{prop.Name}" : prop.Name;

            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var kv in FlattenObject(prop.Value, key))
                        result[kv.Key] = kv.Value;
                    break;

                case JsonValueKind.Array:
                    // Only flatten arrays of scalars (e.g. ["tag1","tag2"] → "tag1|tag2")
                    var scalars = new List<string>();
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind is JsonValueKind.String or JsonValueKind.Number
                            or JsonValueKind.True or JsonValueKind.False)
                        {
                            scalars.Add(item.ToString());
                        }
                    }
                    if (scalars.Count > 0)
                        result[key] = string.Join("|", scalars);
                    break;

                case JsonValueKind.Null or JsonValueKind.Undefined:
                    result[key] = "";
                    break;

                default:
                    result[key] = prop.Value.ToString();
                    break;
            }
        }

        return result;
    }

    private static void WriteCsv(string outputPath, List<string[]> rows)
    {
        using var writer = new StreamWriter(outputPath, append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var row in rows)
            writer.WriteLine(string.Join(",", row.Select(CsvEscape)));
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
