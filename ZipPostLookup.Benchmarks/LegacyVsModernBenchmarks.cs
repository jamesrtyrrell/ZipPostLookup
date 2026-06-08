using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using static ShereSoft.Zip2City;
using ZipPostLookup.Core;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Three-way US lookup comparison isolating the Dictionary vs FrozenDictionary hot path.
///
/// Both dictionaries are built from identical US data (same CodeEntry instances).
/// The only measured variable is the lookup mechanism:
///   • Legacy  — Dictionary&lt;string, CodeEntry&gt; (netstandard2.0 / plain hash table)
///   • Modern  — FrozenDictionary&lt;string, CodeEntry&gt; (net8.0 / read-optimised)
///   • Zip2City — external reference baseline
///
/// US data: ~43k distinct codes.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class LegacyVsModernBenchmarks
{
    private Dictionary<string, CodeEntry>       _legacy = null!;
    private FrozenDictionary<string, CodeEntry> _modern = null!;

    [GlobalSetup]
    public void Setup()
    {
        var registry = new ZipPostRegistry("US");

        var dict = new Dictionary<string, CodeEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in registry.GetAll())
            dict.TryAdd(e.ZpCode, e);

        _legacy = dict;
        _modern = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        _ = GetDefaultCityState("10001"); // warm Zip2City static ctor
    }

    // ── GetByCode hit ─────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "Dictionary       — hit")]
    public CodeEntry? Legacy_Hit() => _legacy.GetValueOrDefault("90210");

    [Benchmark(Description = "FrozenDictionary — hit")]
    public CodeEntry? Modern_Hit() => _modern.GetValueOrDefault("90210");

    [Benchmark(Description = "Zip2City         — hit")]
    public string[]? Zip2City_Hit() => GetDefaultCityState("90210");

    // ── GetByCode miss ────────────────────────────────────────────────────────

    [Benchmark(Description = "Dictionary       — miss")]
    public CodeEntry? Legacy_Miss() => _legacy.GetValueOrDefault("00000");

    [Benchmark(Description = "FrozenDictionary — miss")]
    public CodeEntry? Modern_Miss() => _modern.GetValueOrDefault("00000");

    [Benchmark(Description = "Zip2City         — miss")]
    public string[]? Zip2City_Miss() => GetDefaultCityState("00000");
}
