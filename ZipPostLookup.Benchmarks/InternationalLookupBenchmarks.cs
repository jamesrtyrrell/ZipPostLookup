using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ZipPostLookup.Core;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// International lookup benchmarks — US, CA, and MX registries.
///
/// Zip2City only supports US zip codes. These benchmarks have no Zip2City equivalent
/// and demonstrate ZipPostLookup's international capabilities.
///
/// Key differences from the US-only benchmarks:
///   · CA uses FSA range compression — lookups may resolve via the range index
///   · MX uses 5-digit numeric codes with different state/timezone mappings
///   · Multi-country registry combines all three into one FrozenDictionary
///   · GetByAdmin uses the country schema level names (State/Province/Estado)
///
/// Datasets:
///   US  — 57,400 entries, fully curated
///   CA  — 412,403 entries (after FSA compression from 908,745 rows)
///   MX  — 145,826 entries
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Config(typeof(BenchmarkConfig))]
public class InternationalLookupBenchmarks
{
    private ZipPostRegistry _us = null!;
    private ZipPostRegistry _ca = null!;
    private ZipPostRegistry _mx = null!;
    private ZipPostRegistry _all = null!;

    [GlobalSetup]
    public void Setup()
    {
        _us = new ZipPostRegistry(CountryCode.US);
        _ca = new ZipPostRegistry(CountryCode.CA);
        _mx = new ZipPostRegistry(CountryCode.MX);
        _all = new ZipPostRegistry(CountryCode.US, CountryCode.CA, CountryCode.MX);
    }

    // -------------------------------------------------------------------------
    // US — curated dataset
    // -------------------------------------------------------------------------

    [Benchmark(Baseline = true, Description = "US — GetByCode hit (exact match)")]
    public CodeEntry? US_GetByCode_Hit() =>
        _us.GetByCode("90210");

    [Benchmark(Description = "US — GetByCode miss")]
    public CodeEntry? US_GetByCode_Miss() =>
        _us.GetByCode("00000");

    [Benchmark(Description = "US — GetByState")]
    public IReadOnlyList<CodeEntry> US_GetByState() =>
        _us.GetByState("CA");

    [Benchmark(Description = "US — GetByAdmin (level name)")]
    public IReadOnlyList<CodeEntry> US_GetByAdmin() =>
        _us.GetByAdmin("California", "State");

    [Benchmark(Description = "US — GetByTimeZone")]
    public IReadOnlyList<CodeEntry> US_GetByTimeZone() =>
        _us.GetByTimeZone("America/New_York");

    // -------------------------------------------------------------------------
    // CA — FSA-compressed, range index lookup
    // -------------------------------------------------------------------------

    [Benchmark(Description = "CA — GetByCode (exact LDU — mixed FSA, no range)")]
    public CodeEntry? CA_GetByCode_ExactLdu() =>
        _ca.GetByCode("T0A0A0");   // T0A is mixed (rural AB) — stored as individual LDU

    [Benchmark(Description = "CA — GetByCode (compressed FSA — resolves via range index)")]
    public CodeEntry? CA_GetByCode_RangeLookup() =>
        _ca.GetByCode("M5V3L9");   // M5V is homogeneous (Toronto) — stored as range

    [Benchmark(Description = "CA — GetByCode (compressed FSA — different LDU, same range)")]
    public CodeEntry? CA_GetByCode_RangeLookup_AltLdu() =>
        _ca.GetByCode("M5V7Z9");   // Different LDU in the same M5V range

    [Benchmark(Description = "CA — GetByCode miss")]
    public CodeEntry? CA_GetByCode_Miss() =>
        _ca.GetByCode("Z9Z9Z9");   // Non-existent code

    [Benchmark(Description = "CA — GetByState (province abbreviation)")]
    public IReadOnlyList<CodeEntry> CA_GetByState() =>
        _ca.GetByState("ON");

    [Benchmark(Description = "CA — GetByAdmin (level name: Province)")]
    public IReadOnlyList<CodeEntry> CA_GetByAdmin_Province() =>
        _ca.GetByAdmin("Ontario", "Province");

    [Benchmark(Description = "CA — GetByAdmin (alias: Territory)")]
    public IReadOnlyList<CodeEntry> CA_GetByAdmin_Territory() =>
        _ca.GetByAdmin("Yukon", "Territory");

    [Benchmark(Description = "CA — GetByTimeZone")]
    public IReadOnlyList<CodeEntry> CA_GetByTimeZone() =>
        _ca.GetByTimeZone("America/Toronto");

    // -------------------------------------------------------------------------
    // MX — colonia-level data
    // -------------------------------------------------------------------------

    [Benchmark(Description = "MX — GetByCode hit")]
    public CodeEntry? MX_GetByCode_Hit() =>
        _mx.GetByCode("20000");   // Zona Centro, Aguascalientes

    [Benchmark(Description = "MX — GetByCode miss")]
    public CodeEntry? MX_GetByCode_Miss() =>
        _mx.GetByCode("00000");   // Invalid MX code

    [Benchmark(Description = "MX — GetByState (estado abbreviation)")]
    public IReadOnlyList<CodeEntry> MX_GetByState() =>
        _mx.GetByState("JAL");

    [Benchmark(Description = "MX — GetByAdmin (level name: Estado)")]
    public IReadOnlyList<CodeEntry> MX_GetByAdmin_Estado() =>
        _mx.GetByAdmin("Jalisco", "Estado");

    [Benchmark(Description = "MX — GetByAdmin (alias: State)")]
    public IReadOnlyList<CodeEntry> MX_GetByAdmin_StateAlias() =>
        _mx.GetByAdmin("Jalisco", "State");

    [Benchmark(Description = "MX — GetByTimeZone")]
    public IReadOnlyList<CodeEntry> MX_GetByTimeZone() =>
        _mx.GetByTimeZone("America/Mexico_City");

    // -------------------------------------------------------------------------
    // Multi-country registry — US + CA + MX
    // -------------------------------------------------------------------------

    [Benchmark(Description = "US+CA+MX — GetByCode US zip")]
    public CodeEntry? All_GetByCode_UsZip() =>
        _all.GetByCode("90210");

    [Benchmark(Description = "US+CA+MX — GetByCode CA postal (range resolve)")]
    public CodeEntry? All_GetByCode_CaRange() =>
        _all.GetByCode("M5V3L9");

    [Benchmark(Description = "US+CA+MX — GetByCode MX zip")]
    public CodeEntry? All_GetByCode_MxZip() =>
        _all.GetByCode("20000");

    [Benchmark(Description = "US+CA+MX — GetByTimeZone (spans countries)")]
    public IReadOnlyList<CodeEntry> All_GetByTimeZone_EasternSpansBorders() =>
        _all.GetByTimeZone("America/Toronto");   // Returns both ON and US Eastern entries
}