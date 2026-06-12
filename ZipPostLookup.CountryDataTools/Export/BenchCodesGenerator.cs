namespace ZipPostLookup.CountryDataTools.Export;

/// <summary>
/// Generates <c>ZipPostLookup.Benchmarks/BenchCodes.cs</c> with C# constants derived from
/// the actual exported CSV, so the benchmark probes that depend on the live data stay in sync
/// instead of being computed at runtime in <c>[GlobalSetup]</c>.
///
/// <para>The one value the benchmarks cannot hard-code by hand is a genuine <em>multi-name</em>
/// ZpCode (a code mapped to more than one place). <c>ZpImageBenchmarks</c> used to discover it
/// with <c>GetAll().GroupBy(...)</c> inside <c>[GlobalSetup]</c>, which BenchmarkDotNet re-runs
/// once per benchmark case (≈78 times for that class) and scans the whole dataset every time.
/// Baking it into a generated constant removes that per-case scan while keeping it data-accurate.</para>
///
/// <para>Mirrors <see cref="TestableCodesGenerator"/>: called at the end of every
/// <c>export --target main</c> pipeline run for US, CA, and MX. The per-country values are
/// persisted in <c>ZipPostLookup.Benchmarks/bench-codes.json</c> so each country run updates its
/// own entry while leaving the other countries intact. After updating the JSON the generator
/// rewrites <c>BenchCodes.cs</c> from all entries.</para>
/// </summary>
internal static class BenchCodesGenerator
{
    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    public static async Task UpdateAsync(string repoRoot, string country, string csvPath)
    {
        // Silently skip if the benchmarks project isn't present (e.g. a build agent that only
        // has the library project checked out).
        var benchProjectDir = Path.Combine(repoRoot, "ZipPostLookup.Benchmarks");
        if (!Directory.Exists(benchProjectDir)) return;

        var jsonPath = Path.Combine(benchProjectDir, "bench-codes.json");
        var csPath   = Path.Combine(benchProjectDir, "BenchCodes.cs");

        // Load existing JSON so other countries' values are preserved.
        var data = new Dictionary<string, CountryBenchCodes>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(jsonPath))
        {
            var text     = await File.ReadAllTextAsync(jsonPath);
            var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CountryBenchCodes>>(text);
            if (existing != null)
                foreach (var kv in existing) data[kv.Key] = kv.Value;
        }

        // Pick the multi-name probe from the just-written CSV.
        var (rows, _) = OptimisedCsvSource.Read(csvPath);
        data[country.ToUpperInvariant()] = PickProbes(rows);

        // Persist JSON.
        await File.WriteAllTextAsync(
            jsonPath,
            System.Text.Json.JsonSerializer.Serialize(
                data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            System.Text.Encoding.UTF8);

        // Regenerate BenchCodes.cs.
        await File.WriteAllTextAsync(csPath, GenerateCSharp(data), System.Text.Encoding.UTF8);

        Console.WriteLine($"  ✓ Updated BenchCodes.cs ({country.ToUpperInvariant()} multi probe: " +
            $"{data[country.ToUpperInvariant()].Multi})");
    }

    // -------------------------------------------------------------------------
    // Probe selection
    // -------------------------------------------------------------------------

    private static CountryBenchCodes PickProbes(List<ExportRow> rows)
    {
        // The alphabetically-first non-range ZpCode that maps to more than one row. This is the
        // GetAllByCode (multi-name) probe; it must exist in the data, so we never invent it.
        var multiGroup = rows
            .Where(r => !r.ZpCode.Contains(':'))
            .GroupBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (multiGroup != null)
        {
            var ordered  = multiGroup.OrderByDescending(r => r.IsDefault).ToList();
            return new CountryBenchCodes
            {
                Multi              = multiGroup.Key,
                MultiPrimaryName   = ordered[0].PlaceName,
                MultiSecondaryName = ordered.Count > 1 ? ordered[1].PlaceName : "",
            };
        }

        // No multi-name code in this dataset — fall back to the first exact default code so the
        // benchmark still has a valid GetAllByCode probe (it just won't exercise the multi path).
        var fallback = rows
            .Where(r => !r.ZpCode.Contains(':') && r.IsDefault)
            .OrderBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? rows.First();

        return new CountryBenchCodes
        {
            Multi              = fallback.ZpCode,
            MultiPrimaryName   = fallback.PlaceName,
            MultiSecondaryName = "",
        };
    }

    // -------------------------------------------------------------------------
    // C# source generation
    // -------------------------------------------------------------------------

    private static string GenerateCSharp(Dictionary<string, CountryBenchCodes> data)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// Auto-generated by CountryDataTools export — do not edit manually.");
        sb.AppendLine("// Re-generated on every `export --target main` run for US, CA, and MX.");
        sb.AppendLine("namespace ZipPostLookup.Benchmarks;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Data-derived benchmark probes baked at export time (see BenchCodesGenerator).</summary>");
        sb.AppendLine("internal static class BenchCodes");
        sb.AppendLine("{");

        foreach (var cc in new[] { "US", "CA", "MX" })
        {
            if (!data.TryGetValue(cc, out var codes)) continue;

            sb.AppendLine($"    internal static class {cc}");
            sb.AppendLine("    {");
            sb.AppendLine($"        // Multi-name code: {Comment(codes.MultiPrimaryName)}" +
                (string.IsNullOrEmpty(codes.MultiSecondaryName) ? " (no second name — multi path not exercised)" : $" + {Comment(codes.MultiSecondaryName)}"));
            sb.AppendLine($"        public const string Multi = \"{Esc(codes.Multi)}\";");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // Strip anything that could break out of a single-line comment.
    private static string Comment(string s) => s.Replace("\r", " ").Replace("\n", " ");
}

// ---------------------------------------------------------------------------
// JSON model  (internal — only used by the generator)
// ---------------------------------------------------------------------------

internal sealed class CountryBenchCodes
{
    public string Multi              { get; set; } = "";
    public string MultiPrimaryName   { get; set; } = "";
    public string MultiSecondaryName { get; set; } = "";
}
