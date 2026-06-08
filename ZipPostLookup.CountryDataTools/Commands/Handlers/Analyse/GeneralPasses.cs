using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse;

/// <summary>
/// Country-agnostic analysis passes. These run for every country — including any new
/// country added later that has no <see cref="ICountryAnalysis"/> yet — so a brand-new
/// dataset still gets compression, size, and structure analysis out of the box.
/// </summary>
public static class GeneralPasses
{
    public static IReadOnlyList<IAnalysisPass> All { get; } =
    [
        new OptimalPrefixHomogeneityPass(),
        new ColumnarCardinalityPass(),
        new SizeEstimatePass(),
        new LatLngStrippedPass(),
        new MultiNamePass(),
        new CoverageGapPass(),
    ];

    // ── F01 — Range compression via prefix homogeneity ─────────────────────────
    // Novel element: instead of a hard-coded prefix length per country, sweep depths
    // 1..maxLen and pick the depth that collapses the most rows into homogeneous
    // (same name/tz/admin1) range groups. The winning depth is the natural range key.
    private sealed class OptimalPrefixHomogeneityPass : IAnalysisPass
    {
        public string Id => "F01";

        public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
        {
            if (ctx.TotalRows == 0) yield break;

            var maxLen = ctx.Rows.Min(r => r.ZpCode.Length);
            maxLen = Math.Clamp(maxLen, 1, 5);

            var best = (Len: 0, Groups: 0, HomoRows: 0, HeteroRows: 0, Saved: 0);

            for (var len = 1; len <= maxLen; len++)
            {
                var groups = ctx.GroupByPrefix(len);
                int homoGroups = 0, homoRows = 0, heteroRows = 0;

                foreach (var g in groups)
                {
                    var distinct = g.Select(Signature).Distinct().Count();
                    if (distinct == 1) { homoGroups++; homoRows += g.Count(); }
                    else                 heteroRows += g.Count();
                }

                var saved = homoRows - homoGroups;
                if (saved > best.Saved)
                    best = (len, homoGroups, homoRows, heteroRows, saved);
            }

            if (best.Len == 0) yield break;

            var pct = best.Saved * 100.0 / ctx.TotalRows;

            metrics["homogeneous.prefixLen"]  = best.Len.ToString();
            metrics["homogeneous.groups"]     = best.Groups.ToString("N0");
            metrics["homogeneous.rows"]       = best.HomoRows.ToString("N0");
            metrics["heterogeneous.rows"]     = best.HeteroRows.ToString("N0");
            metrics["homogeneous.rowsSaved"]  = best.Saved.ToString("N0");
            metrics["homogeneous.pct"]        = $"{pct:F1}";

            yield return new Finding(
                Id: Id,
                Category: "Compression",
                Title: $"Range notation for homogeneous code groups (best prefix length: {best.Len})",
                Severity: pct >= 50 ? "High" : pct >= 20 ? "Medium" : "Low",
                Summary:
                    $"Sweeping prefix lengths 1–{maxLen}, a {best.Len}-character prefix maximises " +
                    $"compression: {best.Groups:N0} prefix groups are homogeneous — every row in " +
                    $"the group shares the same place name, timezone, and admin1. These " +
                    $"{best.HomoRows:N0} rows collapse to {best.Groups:N0} range rows, saving " +
                    $"{best.Saved:N0} rows ({pct:F1}% reduction). {best.HeteroRows:N0} rows in mixed " +
                    $"groups must stay individual.",
                AiPrompt:
                    $"For {ctx.Cc} postal-code data, a {best.Len}-character prefix yields {best.Groups:N0} " +
                    $"homogeneous groups (identical place name, timezone, and admin1 across every code " +
                    $"in the group). Propose a range-row format for the embedded CSV that collapses these " +
                    $"into single rows while staying parseable by the existing BuiltInDataSource CSV reader " +
                    $"and the ZipPostRegistry range index. Address: (1) how the range key encodes a " +
                    $"{best.Len}-char prefix span, (2) how the reader distinguishes a range row from an " +
                    $"exact row, (3) lookup precedence when an exact code and a covering range both match, " +
                    $"and (4) round-trip parity with ZpImageLookup.");
        }

        private static (string, string, string, string) Signature(DataReference r) =>
            (r.PlaceName, r.Timezone, r.Admin1, r.Admin1Code);
    }

    // ── F02 — Columnar / dictionary encoding of repeated string columns ────────
    private sealed class ColumnarCardinalityPass : IAnalysisPass
    {
        public string Id => "F02";

        public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
        {
            if (ctx.TotalRows < 10_000) yield break;

            var avgTzLen     = ctx.Rows.Average(r => r.Timezone.Length);
            var tzSaving      = (long)((avgTzLen - 1) * ctx.TotalRows);
            var avgAdmin1Len = ctx.Rows.Average(r => r.Admin1.Length + r.Admin1Code.Length + 1);
            var admin1Saving  = (long)((avgAdmin1Len - 2) * ctx.TotalRows);

            metrics["tz.indexSavingKb"]     = (tzSaving / 1024).ToString("N0");
            metrics["admin1.indexSavingKb"] = (admin1Saving / 1024).ToString("N0");

            if (ctx.UniqueTimezones <= 20)
            {
                yield return new Finding(
                    Id: $"{Id}a",
                    Category: "Size Reduction",
                    Title: "Timezone column is low-cardinality — dictionary-encode it",
                    Severity: ctx.UniqueTimezones <= 5 ? "High" : "Medium",
                    Summary:
                        $"Only {ctx.UniqueTimezones} unique IANA timezone strings across " +
                        $"{ctx.TotalRows:N0} rows (avg ~{avgTzLen:F0} chars each, repeated verbatim). " +
                        $"A single-byte index plus a header lookup table saves ~{tzSaving / 1024:N0} KB.",
                    AiPrompt:
                        $"The {ctx.Cc} CSV has {ctx.UniqueTimezones} unique timezones across " +
                        $"{ctx.TotalRows:N0} rows: " +
                        $"{string.Join(", ", ctx.Rows.Select(r => r.Timezone).Distinct().OrderBy(t => t))}. " +
                        $"Propose a header-encoded index (e.g. #tz:0=America/Toronto,1=America/Vancouver) " +
                        $"where each data row stores the index instead of the string. Cover: " +
                        $"(1) backward compatibility with the current parser, (2) how BuiltInDataSource " +
                        $"detects and decodes the header line, and (3) applying the same scheme to admin1.");
            }

            if (ctx.UniqueAdmin1 <= 50)
            {
                yield return new Finding(
                    Id: $"{Id}b",
                    Category: "Size Reduction",
                    Title: "Admin1 columns are low-cardinality — dictionary-encode them",
                    Severity: ctx.UniqueAdmin1 <= 15 ? "High" : "Medium",
                    Summary:
                        $"Only {ctx.UniqueAdmin1} unique admin1 divisions across {ctx.TotalRows:N0} rows. " +
                        $"Both the admin1 name and code are repeated on every row; indexing them saves " +
                        $"~{admin1Saving / 1024:N0} KB.",
                    AiPrompt:
                        $"The {ctx.Cc} CSV repeats {ctx.UniqueAdmin1} unique admin1 divisions across " +
                        $"{ctx.TotalRows:N0} rows. Combine the timezone and admin1 dictionary schemes into " +
                        $"a single header metadata block. Would a JSON header line be cleaner than a custom " +
                        $"format for BuiltInDataSource to parse? Weigh parse speed against size. NOTE: if " +
                        $"admin1 is fully derivable from the code (see the country derivability finding), " +
                        $"dropping the column entirely beats indexing it.");
            }
        }
    }

    // ── F03 — Size estimates (raw → stripped → indexed → brotli) ───────────────
    private sealed class SizeEstimatePass : IAnalysisPass
    {
        public string Id => "F03";

        public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
        {
            if (ctx.TotalRows == 0) yield break;

            var avgRowBytes = ctx.Rows.Take(1000).Average(r =>
                r.ZpCode.Length + r.PlaceName.Length + r.Timezone.Length +
                r.Admin1.Length + r.Admin1Code.Length + 10 /* delimiters, flags, newline */);

            var raw      = (long)(avgRowBytes * ctx.TotalRows);
            var stripped = (long)((avgRowBytes - 12) * ctx.TotalRows); // lat/lng removed
            long tzSave  = ParseKb(metrics, "tz.indexSavingKb");
            long a1Save  = ParseKb(metrics, "admin1.indexSavingKb");
            var indexed  = Math.Max(0, stripped - tzSave - a1Save);
            var brotli   = (long)(indexed * 0.15); // ~85% on repetitive CSV

            metrics["size.rawKb"]      = (raw / 1024).ToString("N0");
            metrics["size.strippedKb"] = (stripped / 1024).ToString("N0");
            metrics["size.indexedKb"]  = (indexed / 1024).ToString("N0");
            metrics["size.brotliKb"]   = (brotli / 1024).ToString("N0");

            yield return new Finding(
                Id: Id,
                Category: "Compression",
                Title: "Brotli stream compression of the embedded CSV",
                Severity: "Medium",
                Summary:
                    $"Estimated raw CSV ~{raw / 1024:N0} KB → stripped (no lat/lng) " +
                    $"~{stripped / 1024:N0} KB → dictionary-indexed ~{indexed / 1024:N0} KB → " +
                    $"Brotli ~{brotli / 1024:N0} KB. System.IO.Compression.BrotliStream ships with " +
                    $".NET — no package needed.",
                AiPrompt:
                    $"The {ctx.Cc} embedded CSV is estimated at ~{raw / 1024:N0} KB. Propose compressing " +
                    $"it with BrotliStream at export time and decompressing lazily on first access in " +
                    $"BuiltInDataSource. Cover: (1) embedding a .br resource in the .csproj, (2) the " +
                    $"decompression path in BuiltInDataSource, (3) thread-safety of the lazy cache, and " +
                    $"(4) whether to keep the decompressed text in memory. This is exactly what the " +
                    $"ZpImageLookup (.zpi.br) path already does for net8.0+ — compare the two.");
        }

        private static long ParseKb(IDictionary<string, string> m, string key) =>
            m.TryGetValue(key, out var s) && long.TryParse(s.Replace(",", ""), out var v) ? v * 1024 : 0;
    }

    // ── F04 — Lat/Lng stripped unconditionally (informational) ─────────────────
    private sealed class LatLngStrippedPass : IAnalysisPass
    {
        public string Id => "F04";

        public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
        {
            if (ctx.TotalRows == 0) yield break;
            var pct = ctx.RowsWithCoords * 100.0 / ctx.TotalRows;

            yield return new Finding(
                Id: Id,
                Category: "Size Reduction",
                Title: "Lat/Lng stripped unconditionally from the release CSV",
                Severity: "Info",
                Summary:
                    $"Coordinates are held in data.Reference for curation but are not consumed by " +
                    $"ZipPostLookup at runtime (only timezone and admin1 are). The export pipeline " +
                    $"always strips lat/lng. {ctx.RowsWithCoords:N0} of {ctx.TotalRows:N0} rows " +
                    $"({pct:F1}%) currently carry coordinates in the working DB.",
                AiPrompt:
                    $"{ctx.Cc} release CSVs strip lat/lng because ZipPostLookup uses only timezone and " +
                    $"admin1 at runtime. {pct:F1}% of rows have real coordinates in data.Reference. If a " +
                    $"future version needs coordinates, evaluate: (1) an opt-in coordinates sidecar " +
                    $"embedded only when requested, (2) nullable lat/lng on CodeEntry, and (3) the binary " +
                    $"size impact on the default no-coordinates path.");
        }
    }

    // ── F05 — Multi-name codes ─────────────────────────────────────────────────
    private sealed class MultiNamePass : IAnalysisPass
    {
        public string Id => "F05";

        public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
        {
            if (ctx.MultiNameCodes == 0) yield break;

            var avgNames = ctx.ByCode.Where(g => g.Count() > 1).Average(g => (double)g.Count());

            yield return new Finding(
                Id: Id,
                Category: "Data Structure",
                Title: "Multi-name codes — consider name-list encoding",
                Severity: "Low",
                Summary:
                    $"{ctx.MultiNameCodes:N0} codes have multiple place names (avg {avgNames:F1} per code), " +
                    $"stored as separate rows sharing code/timezone/admin1. A pipe-delimited names column " +
                    $"would cut row count but complicate name lookups and IsDefault handling.",
                AiPrompt:
                    $"In {ctx.Cc} data, {ctx.MultiNameCodes:N0} codes have multiple place names stored as " +
                    $"separate rows. Compare: (1) one-row-per-name (current), (2) a pipe-delimited names " +
                    $"column ('A|B|C'), (3) a separate names table keyed by code. Assess impact on " +
                    $"GetByName(), GetByCode() returning the IsDefault primary correctly, and image size.");
        }
    }

    // ── F06 — Coverage gaps (enrichment prioritisation) ────────────────────────
    // Novel element: find prefixes whose code count is far below the median for the
    // country — thin coverage that is a likely target for enrichment / data sourcing.
    private sealed class CoverageGapPass : IAnalysisPass
    {
        public string Id => "F06";

        public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
        {
            if (ctx.TotalRows < 5_000) yield break;

            var len    = metrics.TryGetValue("homogeneous.prefixLen", out var s) && int.TryParse(s, out var l) ? l : 3;
            var groups = ctx.GroupByPrefix(len)
                            .Select(g => (Prefix: g.Key, Codes: g.Select(r => r.ZpCode)
                                                                  .Distinct(StringComparer.OrdinalIgnoreCase).Count()))
                            .OrderBy(g => g.Codes)
                            .ToList();
            if (groups.Count < 4) yield break;

            var median = groups[groups.Count / 2].Codes;
            var thin   = groups.Where(g => g.Codes <= Math.Max(1, median / 5)).ToList();
            if (thin.Count == 0) yield break;

            var sample = string.Join(", ", thin.Take(15).Select(g => $"{g.Prefix}({g.Codes})"));

            yield return new Finding(
                Id: Id,
                Category: "Enrichment",
                Title: "Sparse prefix coverage — enrichment priority targets",
                Severity: "Info",
                Summary:
                    $"{thin.Count:N0} of {groups.Count:N0} {len}-char prefixes have ≤{Math.Max(1, median / 5)} " +
                    $"distinct codes (median is {median}). Thin prefixes are candidates for targeted " +
                    $"enrichment or a data-source gap. Sample: {sample}.",
                AiPrompt:
                    $"For {ctx.Cc}, these {len}-char prefixes have unusually few codes vs the median of " +
                    $"{median}: {sample}. Suggest how to triage them — distinguish genuinely small " +
                    $"prefixes (sparsely populated regions) from incomplete imports, and propose an " +
                    $"enrichment ordering that maximises curated coverage per API call.");
        }
    }
}
