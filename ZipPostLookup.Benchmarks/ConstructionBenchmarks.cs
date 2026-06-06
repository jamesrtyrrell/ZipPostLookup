using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using static ShereSoft.Zip2City;
using ZipPostLookup.Core;
using ZipPostLookup.ZPImage;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Measures the one-time cost of building each registry from its embedded data.
/// This is what a cold application start pays — Lambda cold-start, unit test fixture setup, etc.
///
/// Three startup paths are compared per country:
///   • CSV build — parse the embedded CSV and index into FrozenDictionary indexes (ZipPostRegistry)
///   • ZPI load  — Brotli-decompress the frozen image and memory-map it (ZpImageLookup.FromBuiltIn);
///                 no per-row object construction, so this is the image's headline advantage
///   • Zip2City  — force the static constructor (compile-time baked data), US-only baseline
///
/// Datasets:
///   US  — 57,400 entries (curated)
///   CA  — 412,403 entries (FSA-compressed from 908,745 rows)
///   MX  — 145,826 entries
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Config(typeof(BenchmarkConfig))]
public class ConstructionBenchmarks
{
    // ── ZipPostLookup ─────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "ZipPostLookup — build US registry (57k rows, curated)")]
    public ZipPostRegistry ZipPostLookup_BuildUs() =>
        new ZipPostRegistry(CountryCode.US);

    [Benchmark(Description = "ZipPostLookup — build CA registry (412k rows, FSA-compressed)")]
    public ZipPostRegistry ZipPostLookup_BuildCa() =>
        new ZipPostRegistry(CountryCode.CA);

    [Benchmark(Description = "ZipPostLookup — build MX registry (145k rows)")]
    public ZipPostRegistry ZipPostLookup_BuildMx() =>
        new ZipPostRegistry(CountryCode.MX);

    [Benchmark(Description = "ZipPostLookup — build US+CA combined (North America)")]
    public ZipPostRegistry ZipPostLookup_BuildNorthAmerica() =>
        new ZipPostRegistry(CountryCode.US, CountryCode.CA);

    [Benchmark(Description = "ZipPostLookup — build US+CA+MX combined (North America full)")]
    public ZipPostRegistry ZipPostLookup_BuildNorthAmericaFull() =>
        new ZipPostRegistry(CountryCode.US, CountryCode.CA, CountryCode.MX);

    // ── ZP frozen image — Brotli-decompress + map, no per-row construction ──────

    [Benchmark(Description = "ZP image — load US frozen image (Brotli)")]
    public ZpImageLookup ZpImage_LoadUs() =>
        ZpImageLookup.FromBuiltIn(CountryCode.US);

    [Benchmark(Description = "ZP image — load CA frozen image (Brotli)")]
    public ZpImageLookup ZpImage_LoadCa() =>
        ZpImageLookup.FromBuiltIn(CountryCode.CA);

    [Benchmark(Description = "ZP image — load MX frozen image (Brotli)")]
    public ZpImageLookup ZpImage_LoadMx() =>
        ZpImageLookup.FromBuiltIn(CountryCode.MX);

    // ── Zip2City ──────────────────────────────────────────────────────────────
    // Zip2City stores all data in a static Dictionary<string, string[][]> initialised once.
    // There is no registry constructor — access any member to force type initialisation.

    [Benchmark(Description = "Zip2City — force type initialisation via first lookup")]
    public string[]? Zip2City_ForceInit() =>
        // Touching any member forces the static ctor.
        GetDefaultCityState("90210");
}