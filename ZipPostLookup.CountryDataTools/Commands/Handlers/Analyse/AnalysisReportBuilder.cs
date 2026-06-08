using System.Text;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse;

/// <summary>
/// Renders the Markdown analysis report from an <see cref="AnalysisContext"/>, the findings
/// emitted by the passes, and the shared metrics bag.
/// </summary>
public sealed class AnalysisReportBuilder
{
    private readonly AnalysisContext _ctx;
    private readonly IReadOnlyList<Finding> _findings;
    private readonly IReadOnlyDictionary<string, string> _metrics;
    private readonly StringBuilder _sb = new();

    public AnalysisReportBuilder(
        AnalysisContext ctx,
        IReadOnlyList<Finding> findings,
        IReadOnlyDictionary<string, string> metrics)
    {
        _ctx      = ctx;
        _findings = findings;
        _metrics  = metrics;
    }

    public string Build()
    {
        var now = DateTime.UtcNow;
        var ctx = _ctx;

        Line($"# ZipPostLookup — Data Analysis Report: {ctx.Cc}");
        Line();
        Line($"**Country:** {ctx.CountryName} (`{ctx.Cc}`)  ");
        Line($"**Generated:** {now:yyyy-MM-dd HH:mm} UTC  ");
        Line($"**Source:** `data.Reference` — curated (`Curated = 1`) and non-flagged (`Flagged = 0`) rows only  ");
        Line($"**Rows analysed:** {ctx.TotalRows:N0}  ");
        Line();
        Line("---");
        Line();

        // 1. Overview
        Line("## 1. Dataset Overview");
        Line();
        Line("| Metric | Value |");
        Line("|--------|-------|");
        Line($"| Rows (curated, non-flagged) | {ctx.TotalRows:N0} |");
        Line($"| Unique codes | {ctx.UniqueCodes:N0} |");
        Line($"| Unique place names | {ctx.UniqueNames:N0} |");
        Line($"| Unique IANA timezones | {ctx.UniqueTimezones} |");
        Line($"| Unique admin1 divisions | {ctx.UniqueAdmin1} |");
        Line($"| Rows with coordinates | {ctx.RowsWithCoords:N0} ({Pct(ctx.RowsWithCoords, ctx.TotalRows)}) |");
        Line($"| Single-name codes | {ctx.SingleNameCodes:N0} |");
        Line($"| Multi-name codes | {ctx.MultiNameCodes:N0} |");
        Line();

        // 2. Derivability scorecard (the headline)
        Line("## 2. Derivability Scorecard");
        Line();
        if (_metrics.TryGetValue("admin1.derivable.matchPct", out var matchPct))
        {
            Line("How much of the stored data is a pure function of the code (and therefore need not be shipped).");
            Line();
            Line("| Attribute | Decidable | Matches rule | Conflicts |");
            Line("|-----------|-----------|--------------|-----------|");
            Line($"| Admin1 | {_metrics.GetValueOrDefault("admin1.derivable.decidablePct", "—")}% of rows | " +
                 $"{matchPct}% | {_metrics.GetValueOrDefault("admin1.derivable.conflicts", "—")} |");
            Line();
            Line("> A high admin1 match rate means the admin1 name/code columns can be **dropped from the " +
                 "shipped image** and derived at load time, beating dictionary indexing. See finding C01.");
        }
        else
        {
            Line($"No deterministic admin1 derivation is available for {ctx.Cc} " +
                 $"(`ICountryRules.ResolveAdmin1` not overridden and no analysis-layer rule). " +
                 $"Admin1 must be shipped or dictionary-indexed.");
        }
        Line();

        // Timezone distribution
        Line("### Timezone Distribution");
        Line();
        Line("| Timezone | Rows | % |");
        Line("|----------|------|---|");
        foreach (var tz in ctx.Rows
                     .GroupBy(r => r.Timezone, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Count()))
            Line($"| `{tz.Key}` | {tz.Count():N0} | {Pct(tz.Count(), ctx.TotalRows)} |");
        Line();

        // Admin1 distribution
        Line("### Admin1 Division Distribution");
        Line();
        Line("| Code | Name | Rows | Unique Codes |");
        Line("|------|------|------|--------------|");
        foreach (var div in ctx.Rows
                     .GroupBy(r => r.Admin1Code, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Count()))
        {
            var name     = div.First().Admin1;
            var codeCount = div.Select(r => r.ZpCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Line($"| `{div.Key}` | {name} | {div.Count():N0} | {codeCount:N0} |");
        }
        Line();

        // 3. Size estimates
        Line("## 3. Size Estimates");
        Line();
        Line("Estimated from average row character widths sampled from the dataset.");
        Line();
        Line("| Variant | Est. Size | Notes |");
        Line("|---------|-----------|-------|");
        Line($"| Raw reference CSV | ~{_metrics.GetValueOrDefault("size.rawKb", "—")} KB | All columns incl. lat/lng |");
        Line($"| Stripped (no lat/lng) | ~{_metrics.GetValueOrDefault("size.strippedKb", "—")} KB | Always applied — coords unused at runtime |");
        Line($"| Dictionary-indexed | ~{_metrics.GetValueOrDefault("size.indexedKb", "—")} KB | tz + admin1 replaced by index |");
        Line($"| Brotli compressed | ~{_metrics.GetValueOrDefault("size.brotliKb", "—")} KB | ~85% on repetitive CSV |");
        Line();

        // 4. Compression opportunities
        Line("## 4. Compression Opportunities");
        Line();
        Line("| Metric | Value |");
        Line("|--------|-------|");
        Line($"| Best prefix length (range key) | {_metrics.GetValueOrDefault("homogeneous.prefixLen", "—")} |");
        Line($"| Homogeneous prefix groups | {_metrics.GetValueOrDefault("homogeneous.groups", "—")} |");
        Line($"| Rows representable as range rows | {_metrics.GetValueOrDefault("homogeneous.rows", "—")} |");
        Line($"| Heterogeneous rows (stay individual) | {_metrics.GetValueOrDefault("heterogeneous.rows", "—")} |");
        Line($"| Potential range compression | {_metrics.GetValueOrDefault("homogeneous.pct", "—")}% row reduction |");
        Line();

        // 5. Findings
        Line("## 5. Findings");
        Line();
        Line("Each finding includes a self-contained AI prompt tailored to this dataset.");
        Line();
        foreach (var f in _findings.OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            var badge = f.Severity switch
            {
                "High"   => "🔴 High",
                "Medium" => "🟡 Medium",
                "Info"   => "🔵 Info",
                _        => "🟢 Low",
            };

            Line($"### {f.Id} — {f.Title}");
            Line();
            Line($"**Category:** {f.Category}  ");
            Line($"**Priority:** {badge}  ");
            Line();
            Line("**Finding:**  ");
            Line(f.Summary);
            Line();
            Line("**AI Prompt:**");
            Line();
            Line("```");
            Line(f.AiPrompt);
            Line("```");
            Line();
        }

        // 6. Combined prompt
        Line("## 6. Combined Optimisation Prompt");
        Line();
        Line("Paste into an AI assistant for a holistic plan:");
        Line();
        Line("```");
        Line($"I maintain ZipPostLookup (.NET 8+), a postal-code library with embedded CSV data for");
        Line($"{ctx.CountryName} ({ctx.Cc}). The curated, non-flagged dataset has {ctx.TotalRows:N0} rows,");
        Line($"{ctx.UniqueCodes:N0} unique codes, {ctx.UniqueTimezones} timezones, and {ctx.UniqueAdmin1} admin1 divisions.");
        Line();
        Line("Key characteristics:");
        Line($"- Lat/lng always stripped from the release CSV (runtime uses only timezone + admin1)");
        if (_metrics.TryGetValue("admin1.derivable.matchPct", out var mp))
            Line($"- Admin1 is {mp}% derivable from the code alone — candidate to drop from the image");
        Line($"- Best range-key prefix length is {_metrics.GetValueOrDefault("homogeneous.prefixLen", "?")}, " +
             $"giving ~{_metrics.GetValueOrDefault("homogeneous.pct", "?")}% row reduction");
        Line($"- Only {ctx.UniqueTimezones} timezones and {ctx.UniqueAdmin1} admin1 values, repeated across rows");
        Line($"- {ctx.MultiNameCodes:N0} codes have multiple place names");
        Line();
        Line($"Runtime: BuiltInDataSource reads the embedded CSV into a ZipPostRegistry (FrozenDictionary");
        Line($"indexes); ZpImageLookup reads an equivalent Brotli-compressed binary image (.zpi.br) on");
        Line($"net8.0+. The export pipeline (ExportReferenceCommand + stages) runs pre-pack transforms.");
        Line($"Lookup parity between the CSV and image paths is enforced by ZpImageParityTests.");
        Line();
        Line($"Propose a prioritised plan covering admin1 derivation-at-load, range compression, string");
        Line($"indexing, and Brotli. For each: estimated size saving and implementation complexity.");
        Line($"The public API (CodeEntry, ZipPostRegistry, IZipPostLookup) must not change.");
        Line("```");
        Line();
        Line("---");
        Line();
        Line($"*Report generated by `CountryDataTools analyse --country {ctx.Cc}` on {now:yyyy-MM-dd}.*");

        return _sb.ToString();
    }

    private static string Pct(int n, int total) => total == 0 ? "0%" : $"{n * 100.0 / total:F1}%";

    private void Line(string text = "") => _sb.AppendLine(text);
}
