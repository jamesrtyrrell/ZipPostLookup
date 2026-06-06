namespace ZipPostLookup.CountryDataTools.Export;

/// <summary>
/// Generates <c>ZipPostLookup.Tests/TestCodes.cs</c> with C# constants derived from
/// the actual exported CSV, so <c>ZpImageParityTests</c> probes are always in sync with the
/// live data instead of being hard-coded strings that silently go stale.
///
/// <para>Called at the end of every <c>export --target main</c> pipeline run for US, CA, and MX.
/// The per-country values are persisted in <c>ZipPostLookup.Tests/testable-codes.json</c> so
/// each country run updates its own entry while leaving the other countries intact.
/// After updating the JSON the generator rewrites <c>TestCodes.cs</c> from all entries.</para>
/// </summary>
internal static class TestableCodesGenerator
{
    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    public static async Task UpdateAsync(string repoRoot, string country, string csvPath)
    {
        // Silently skip if the test project doesn't exist (e.g. build agent that only
        // has the library project checked out).
        var testProjectDir = Path.Combine(repoRoot, "ZipPostLookup.Tests");
        if (!Directory.Exists(testProjectDir)) return;

        var jsonPath = Path.Combine(testProjectDir, "testable-codes.json");
        var csPath   = Path.Combine(testProjectDir, "TestCodes.cs");

        // Load existing JSON so other countries' values are preserved.
        var data = new Dictionary<string, CountryTestCodes>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(jsonPath))
        {
            var text     = await File.ReadAllTextAsync(jsonPath);
            var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CountryTestCodes>>(text);
            if (existing != null)
                foreach (var kv in existing) data[kv.Key] = kv.Value;
        }

        // Pick probes from the just-written CSV.
        var (rows, _) = OptimisedCsvSource.Read(csvPath);
        var codes     = PickProbes(country, rows);
        data[country.ToUpperInvariant()] = codes;

        // Persist JSON.
        await File.WriteAllTextAsync(
            jsonPath,
            System.Text.Json.JsonSerializer.Serialize(
                data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            System.Text.Encoding.UTF8);

        // Regenerate TestCodes.cs.
        await File.WriteAllTextAsync(csPath, GenerateCSharp(data), System.Text.Encoding.UTF8);

        Console.WriteLine($"  ✓ Updated TestCodes.cs ({country.ToUpperInvariant()} probes: {codes.ExactCode}" +
            (codes.RangeProbe != null ? $", range: {codes.RangeProbe}" : "") + ")");
    }

    // -------------------------------------------------------------------------
    // Probe selection
    // -------------------------------------------------------------------------

    private static CountryTestCodes PickProbes(string country, List<ExportRow> rows)
    {
        // Candidate exact rows: non-range, IsDefault=true, non-empty name and admin code.
        var exactRows = rows
            .Where(r => !r.ZpCode.Contains(':') && r.IsDefault &&
                        !string.IsNullOrEmpty(r.PlaceName) &&
                        !string.IsNullOrEmpty(r.Admin1Code) &&
                        r.Admin1Code != "---")
            .OrderBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var exact  = exactRows.FirstOrDefault() ?? rows.First();
        var result = new CountryTestCodes
        {
            ExactCode       = exact.ZpCode,
            ExactPlaceName  = exact.PlaceName,
            ExactAdmin1Code = exact.Admin1Code ?? "",
            ExactTimezone   = exact.Timezone   ?? "",
        };

        // Range probe — only meaningful for CA (the only built-in country with range rows).
        var rangeRows = rows.Where(r => r.ZpCode.Contains(':')).ToList();
        if (rangeRows.Count > 0)
        {
            var rangeRow = rangeRows
                .OrderBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase)
                .First();

            var probe = BuildRangeProbe(rangeRow.ZpCode, exactRows);
            if (probe != null)
            {
                result.RangeProbe      = probe;
                result.RangeZpCode     = rangeRow.ZpCode;
                result.RangePlaceName  = rangeRow.PlaceName;
                result.RangeAdmin1Code = rangeRow.Admin1Code ?? "";
                result.RangeTimezone   = rangeRow.Timezone   ?? "";
            }
        }

        return result;
    }

    private static string? BuildRangeProbe(string rangeZpCode, List<ExportRow> exactRows)
    {
        // Format: "A1E0**:A1E6**"  →  split on ':', take [0] and [1].
        var colon = rangeZpCode.IndexOf(':');
        if (colon < 4) return null;

        var start = rangeZpCode[..colon];      // e.g. "A1E0**"
        var end   = rangeZpCode[(colon + 1)..]; // e.g. "A1E6**"

        if (start.Length < 4 || end.Length < 4) return null;

        var prefix = start[..3]; // "A1E"
        var start4 = start[3];   // '0'
        var end4   = end[3];     // '6'

        // Compute mid character once; try several suffixes until we find a code that has
        // no exact row (range-only fallback path is only exercised when there is no exact match).
        var mid4     = (char)((start4 + end4) / 2);
        var exactSet = exactRows.Select(r => r.ZpCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var suffix in new[] { "H0", "Z9", "A0", "B0" })
        {
            var probe = prefix + mid4 + suffix;
            if (!exactSet.Contains(probe)) return probe;
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // C# source generation
    // -------------------------------------------------------------------------

    private static string GenerateCSharp(Dictionary<string, CountryTestCodes> data)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// Auto-generated by CountryDataTools export — do not edit manually.");
        sb.AppendLine("// Re-generated on every `export --target main` run for US, CA, and MX.");
        sb.AppendLine("namespace ZipPostLookup.Tests;");
        sb.AppendLine();
        sb.AppendLine("internal static class TestCodes");
        sb.AppendLine("{");

        foreach (var cc in new[] { "US", "CA", "MX" })
        {
            if (!data.TryGetValue(cc, out var codes)) continue;

            sb.AppendLine($"    internal static class {cc}");
            sb.AppendLine("    {");
            sb.AppendLine($"        public const string ExactCode       = \"{Esc(codes.ExactCode)}\";");
            sb.AppendLine($"        public const string ExactPlaceName  = \"{Esc(codes.ExactPlaceName)}\";");
            sb.AppendLine($"        public const string ExactAdmin1Code = \"{Esc(codes.ExactAdmin1Code)}\";");
            sb.AppendLine($"        public const string ExactTimezone   = \"{Esc(codes.ExactTimezone)}\";");

            if (codes.RangeProbe != null)
            {
                sb.AppendLine($"        public const string RangeProbe      = \"{Esc(codes.RangeProbe)}\";");
                sb.AppendLine($"        public const string RangeZpCode     = \"{Esc(codes.RangeZpCode ?? "")}\";");
                sb.AppendLine($"        public const string RangePlaceName  = \"{Esc(codes.RangePlaceName ?? "")}\";");
                sb.AppendLine($"        public const string RangeAdmin1Code = \"{Esc(codes.RangeAdmin1Code ?? "")}\";");
                sb.AppendLine($"        public const string RangeTimezone   = \"{Esc(codes.RangeTimezone ?? "")}\";");
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

// ---------------------------------------------------------------------------
// JSON model  (internal — only used by the generator)
// ---------------------------------------------------------------------------

internal sealed class CountryTestCodes
{
    public string  ExactCode       { get; set; } = "";
    public string  ExactPlaceName  { get; set; } = "";
    public string  ExactAdmin1Code { get; set; } = "";
    public string  ExactTimezone   { get; set; } = "";
    public string? RangeProbe      { get; set; }
    public string? RangeZpCode     { get; set; }
    public string? RangePlaceName  { get; set; }
    public string? RangeAdmin1Code { get; set; }
    public string? RangeTimezone   { get; set; }
}
