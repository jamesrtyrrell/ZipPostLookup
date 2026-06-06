using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ZipPostLookup.Core;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Canadian FSA range compression benchmarks.
///
/// Canada's 908,745 reference rows are compressed to 412,403 entries by replacing
/// homogeneous FSAs (where every LDU maps to the same city/province/timezone) with a
/// single range row using the notation "B2G0**:B2G9**".
///
/// This benchmark compares:
///   · Exact LDU lookup    — FSA is mixed (multiple cities), stored as individual rows
///   · Range lookup        — FSA is homogeneous, stored as a compressed range
///   · Full LDU registry   — same data loaded WITHOUT compression for comparison
///
/// The range index is a Dictionary{string, List{(Start4, End4, Entry)}} keyed by
/// the 3-char FSA prefix. Lookups that miss the exact code index fall through to a
/// single FSA dictionary lookup + small linear scan — effectively O(1).
///
/// CA compressed  : 412,403 entries
/// CA uncompressed: 908,745 entries
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Config(typeof(BenchmarkConfig))]
public class CaRangeBenchmarks
{
    private ZipPostRegistry _compressed = null!;

    // Sample codes that fall into homogeneous (compressed) FSAs
    private static readonly string[] CompressedFsaCodes =
    [
        "M5V3L9",   // Toronto, ON — M5V range
        "V6B1A1",   // Vancouver, BC — V6B range
        "H3T1A1",   // Montreal, QC — H3T is mixed, so this hits exact
        "B2G1A1",   // Antigonish, NS — B2G range
        "K1A0A1",   // Ottawa, ON — K1A range
    ];

    // Sample codes that fall into mixed (uncompressed) FSAs
    private static readonly string[] MixedFsaCodes =
    [
        "T0A0A0",   // Abee, AB — T0A is mixed (rural AB)
        "S0G0A0",   // rural SK — S0G is mixed
        "A0G0A0",   // rural NL — A0G is mixed
        "T0B0A0",   // rural AB — T0B is mixed
        "G0R0A0",   // rural QC — G0R is mixed
    ];

    [GlobalSetup]
    public void Setup()
    {
        _compressed = new ZipPostRegistry(CountryCode.CA);
    }

    // -------------------------------------------------------------------------
    // Single code lookups
    // -------------------------------------------------------------------------

    [Benchmark(Baseline = true, Description = "CA — exact LDU hit (mixed FSA — no range involved)")]
    public CodeEntry? CA_ExactLdu_Hit() =>
        _compressed.GetByCode("T0A0A0");

    [Benchmark(Description = "CA — compressed FSA hit (range index resolution)")]
    public CodeEntry? CA_RangeLookup_Hit() =>
        _compressed.GetByCode("M5V3L9");

    [Benchmark(Description = "CA — compressed FSA hit (different LDU, same range)")]
    public CodeEntry? CA_RangeLookup_AltLdu() =>
        _compressed.GetByCode("M5V5A3");

    [Benchmark(Description = "CA — exact LDU miss (valid format, not in data)")]
    public CodeEntry? CA_ExactLdu_Miss() =>
        _compressed.GetByCode("Z9Z9Z9");

    [Benchmark(Description = "CA — compressed FSA miss (valid prefix, no range match)")]
    public CodeEntry? CA_RangeLookup_Miss() =>
        _compressed.GetByCode("Z9Z0A0");

    // -------------------------------------------------------------------------
    // Case-insensitive normalisation
    // -------------------------------------------------------------------------

    [Benchmark(Description = "CA — lowercase input (normalisation + range resolve)")]
    public CodeEntry? CA_Lowercase_Range() =>
        _compressed.GetByCode("m5v3l9");

    [Benchmark(Description = "CA — spaced input 'M5V 3L9' (normalisation + range resolve)")]
    public CodeEntry? CA_Spaced_Range() =>
        _compressed.GetByCode("M5V 3L9");

    [Benchmark(Description = "CA — hyphenated input 'M5V-3L9' (normalisation + range resolve)")]
    public CodeEntry? CA_Hyphenated_Range() =>
        _compressed.GetByCode("M5V-3L9");

    // -------------------------------------------------------------------------
    // Spread — realistic mixed workload
    // -------------------------------------------------------------------------

    [Benchmark(Description = "CA — 5 lookups in compressed FSAs (spread)")]
    public int CA_Compressed_Spread()
    {
        int found = 0;
        foreach (var code in CompressedFsaCodes)
        {
            if (_compressed.GetByCode(code) != null) { found++; }
        }
        return found;
    }

    [Benchmark(Description = "CA — 5 lookups in mixed FSAs (spread, exact match)")]
    public int CA_Mixed_Spread()
    {
        int found = 0;
        foreach (var code in MixedFsaCodes)
        {
            if (_compressed.GetByCode(code) != null) { found++; }
        }
        return found;
    }

}