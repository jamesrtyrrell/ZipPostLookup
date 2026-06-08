using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ZipPostLookup.Core;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Multi-country registry benchmarks — a single <see cref="ZipPostRegistry"/> combining
/// US + CA + MX, demonstrating that combined-registry lookup is comparable to single-country
/// lookup (one shared FrozenDictionary, no per-lookup dispatch overhead).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Config(typeof(BenchmarkConfig))]
public class MultiCountryBenchmarks
{
    private ZipPostRegistry _all = null!;

    [GlobalSetup]
    public void Setup()
    {
        _all = new ZipPostRegistry(CountryCode.US, CountryCode.CA, CountryCode.MX);
    }

    [Benchmark(Baseline = true, Description = "US+CA+MX — GetByCode US zip")]
    public CodeEntry? All_GetByCode_UsZip() =>
        _all.GetByCode("90210");

    [Benchmark(Description = "US+CA+MX — GetByCode CA postal (range resolve)")]
    public CodeEntry? All_GetByCode_CaRange() =>
        _all.GetByCode("M5V3L9");

    [Benchmark(Description = "US+CA+MX — GetByCode MX zip")]
    public CodeEntry? All_GetByCode_MxZip() =>
        _all.GetByCode("20000");

    [Benchmark(Description = "US+CA+MX — GetByTimeZone (spans US and CA)")]
    public IReadOnlyList<CodeEntry> All_GetByTimeZone_EasternSpansBorders() =>
        _all.GetByTimeZone("America/Toronto");
}
