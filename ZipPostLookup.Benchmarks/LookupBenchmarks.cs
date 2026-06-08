using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using static ShereSoft.Zip2City;
using ZipPostLookup.Core;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Hot-path lookup benchmarks — ZipPostLookup (CSV registry) vs Zip2City for US codes.
/// The registry is fully warmed up in GlobalSetup before any measurement begins.
///
/// Comparable operations:
///   ZipPostLookup.GetByCode    ↔  Zip2City.GetDefaultCityState
///   ZipPostLookup.GetAllByCode ↔  Zip2City.GetAllCityStates
///   ZipPostLookup.GetClosest   ↔  Zip2City.GetClosestCityState
///
/// Zip2City only supports US zips; reverse lookups have no equivalent.
/// US data: 57,400 rows — fully curated.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Config(typeof(BenchmarkConfig))]
public class LookupBenchmarks
{
    private ZipPostRegistry _postRegistry = null!;

    // Required by BenchmarkDotNet to properly consume deferred IEnumerable results
    private readonly Consumer _consumer = new();

    [GlobalSetup]
    public void Setup()
    {
        _postRegistry = new ZipPostRegistry(CountryCode.US);

        // Warm up Zip2City's static type initialiser so it pays no cost during measurement
        _ = GetDefaultCityState("10001");
    }

    // ── Single code lookup — hit ───────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "ZipPostLookup — GetByCode (hit)")]
    public CodeEntry? ZipPostLookup_GetByCode_Hit() =>
        _postRegistry.GetByCode("90210");

    [Benchmark(Description = "Zip2City — GetDefaultCityState (hit)")]
    public string[]? Zip2City_GetDefaultCityState_Hit() =>
        GetDefaultCityState("90210");

    // ── Single code lookup — miss ──────────────────────────────────────────────

    [Benchmark(Description = "ZipPostLookup — GetByCode (miss)")]
    public CodeEntry? ZipPostLookup_GetByCode_Miss() =>
        _postRegistry.GetByCode("00000");

    [Benchmark(Description = "Zip2City — GetDefaultCityState (miss)")]
    public string[]? Zip2City_GetDefaultCityState_Miss() =>
        GetDefaultCityState("00000");

    // ── All entries for a code (multi-city) ───────────────────────────────────

    [Benchmark(Description = "ZipPostLookup — GetAllByCode (multi-city)")]
    public IReadOnlyList<CodeEntry> ZipPostLookup_GetAllByCode() =>
        _postRegistry.GetAllByCode("00603");   // Aguadilla + Ramey, Puerto Rico

    [Benchmark(Description = "Zip2City — GetAllCityStates")]
    public void Zip2City_GetAllCityStates() =>
        GetAllCityStates("00603").Consume(_consumer);

    // ── Closest match ─────────────────────────────────────────────────────────

    [Benchmark(Description = "ZipPostLookup — GetClosest (no exact match)")]
    public CodeEntry? ZipPostLookup_GetClosest() =>
        _postRegistry.GetClosest("00001");

    [Benchmark(Description = "Zip2City — GetClosestCityState (no exact match)")]
    public string[]? Zip2City_GetClosestCityState() =>
        GetClosestCityState("00001");
}
