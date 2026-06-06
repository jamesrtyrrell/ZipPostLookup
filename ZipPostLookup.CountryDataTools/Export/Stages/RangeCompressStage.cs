using ZipPostLookup.Core;

namespace ZipPostLookup.CountryDataTools.Export.Stages;

/// <summary>
/// Collapses homogeneous prefix groups into a single range row.
///
/// A group is homogeneous when every row in it shares the same
/// Name, Timezone, Admin1, and Admin1Code.  Such groups are encoded as
/// a single row with a range code (<c>T0A0**:T0A9**</c>) that
/// <see cref="ZipPostRegistry"/> expands at load time.
///
/// Grouping prefix lengths:
///   CA, US — first 3 characters (FSA / 3-digit SCF prefix)
///   MX     — first 2 characters (INEGI state block)
///
/// Range code format:
///   <c>{start4}**:{end4}**</c>  where start4/end4 are the first 4 characters
///   of the lexicographically smallest/largest code in the group.
///   The <c>**</c> suffix signals to <c>ZipRegistry.LookupByRange</c> that
///   this is a prefix pattern, not an exact code.
///
/// Only groups with 2+ rows benefit from compression; single-row groups are
/// emitted unchanged.
///
/// <para>
/// <b>Significance gate.</b> Range rows are not free: at lookup time a code that
/// only matches a range no longer hits the O(1) exact-key fast path in
/// <c>ZipPostRegistry.NormalizeAndLookup</c> — it falls through the normalisation
/// loop into <c>LookupByRange</c>, which allocates on every call.  That cost is
/// only worth paying when range compression removes a large share of the rows.
/// Per the data-analysis reports it does for CA (~61.5% row reduction) but barely
/// for US (~1.7%) and not at all for MX (0%).  So compression is only kept when the
/// reduction reaches <see cref="_minReductionRatio"/>; otherwise every code is
/// emitted as an exact row and stays on the fast path.
/// </para>
/// </summary>
internal sealed class RangeCompressStage : IExportStage
{
    /// <summary>
    /// Minimum fraction of rows that range compression must eliminate before it is
    /// kept.  Below this, the size win does not justify pushing those codes off the
    /// exact-key lookup fast path, so the stage is a no-op.  Chosen to sit well above
    /// the US (~1.7%) / MX (0%) candidates and well below CA (~61.5%).
    /// </summary>
    private const double DefaultMinReductionRatio = 0.20;

    private readonly int    _groupPrefixLen;
    private readonly double _minReductionRatio;

    public string StageName => "Range Compress";

    public RangeCompressStage(string countryCode, double minReductionRatio = DefaultMinReductionRatio)
    {
        _groupPrefixLen    = countryCode.ToUpperInvariant() == "MX" ? 2 : 3;
        _minReductionRatio = minReductionRatio;
    }

    public (List<ExportRow> Rows, ExportMeta Meta) Apply(
        List<ExportRow> rows, ExportMeta meta)
    {
        var result         = new List<ExportRow>(rows.Count);
        var groupsCompressed = 0;
        var rowsSaved      = 0;

        // Group by FSA prefix
        var groups = new Dictionary<string, List<ExportRow>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var key = row.ZpCode.Length >= _groupPrefixLen
                ? row.ZpCode[.._groupPrefixLen]
                : row.ZpCode;

            if (!groups.TryGetValue(key, out var bucket))
            {
                bucket = new List<ExportRow>(4);
                groups[key] = bucket;
            }

            bucket.Add(row);
        }

        foreach (var (_, bucket) in groups)
        {
            if (bucket.Count < 2)
            {
                result.AddRange(bucket);
                continue;
            }

            // Test homogeneity — all rows share the same data fields
            var first = bucket[0];
            var homogeneous = true;

            for (var i = 1; i < bucket.Count; i++)
            {
                var r = bucket[i];
                if (!string.Equals(r.PlaceName,   first.PlaceName,  StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(r.Timezone,    first.Timezone,   StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(r.Admin1,      first.Admin1,     StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(r.Admin1Code,  first.Admin1Code, StringComparison.OrdinalIgnoreCase))
                {
                    homogeneous = false;
                    break;
                }
            }

            if (!homogeneous)
            {
                result.AddRange(bucket);
                continue;
            }

            // Build range code from first 4 chars of min/max codes in the group
            bucket.Sort((a, b) =>
                string.Compare(a.ZpCode, b.ZpCode, StringComparison.OrdinalIgnoreCase));

            var minCode   = bucket[0].ZpCode;
            var maxCode   = bucket[^1].ZpCode;
            var prefixLen = Math.Min(4, minCode.Length);
            var wildcards = new string('*', Math.Max(0, minCode.Length - prefixLen));
            var start4    = minCode[..prefixLen];
            var end4      = maxCode[..prefixLen];
            var rangeCode = $"{start4}{wildcards}:{end4}{wildcards}";

            result.Add(new ExportRow
            {
                ZpCode     = rangeCode,
                PlaceName  = first.PlaceName,
                Timezone   = first.Timezone,
                IsDefault  = true,              // range rows are always the default
                Lat        = first.Lat,
                Lng        = first.Lng,
                Admin1     = first.Admin1,
                Admin1Code = first.Admin1Code,
            });

            groupsCompressed++;
            rowsSaved += bucket.Count - 1;
        }

        // Significance gate: only keep the compressed result if it removes a large
        // enough share of the rows. Otherwise emit every code as an exact row so it
        // stays on the O(1) exact-key fast path and never pays the range-fallback
        // allocation cost at lookup time.
        var reduction = rows.Count == 0 ? 0d : (double)rowsSaved / rows.Count;

        if (reduction < _minReductionRatio)
        {
            var passthrough = new List<ExportRow>(rows);
            passthrough.Sort((a, b) =>
                string.Compare(a.ZpCode, b.ZpCode, StringComparison.OrdinalIgnoreCase));

            Console.WriteLine(
                $"    {StageName}: skipped — {reduction:P1} row reduction is below the " +
                $"{_minReductionRatio:P0} significance threshold; keeping all " +
                $"{rows.Count:N0} codes as exact rows (fast-path preserved).");

            return (passthrough, meta);
        }

        // Preserve stable output order: ranges first (sorted), then individuals (sorted)
        result.Sort((a, b) =>
            string.Compare(a.ZpCode, b.ZpCode, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine(
            $"    {StageName}: {groupsCompressed:N0} groups compressed, " +
            $"{rowsSaved:N0} rows saved " +
            $"({rows.Count:N0} → {result.Count:N0} rows, {reduction:P1} reduction).");

        return (result, meta);
    }
}
