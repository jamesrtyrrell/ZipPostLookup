using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using static ShereSoft.Zip2City;
using ZipPostLookup.Core;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Hot-path lookup benchmarks — both registries are fully warmed up in GlobalSetup
/// before any measurement begins.
///
/// Comparable operations:
///
///   ZipPostLookup.GetByZip        ↔  Zip2City.GetDefaultCityState
///   ZipPostLookup.TryGetByZip     ↔  Zip2City.GetDefaultCityState (null-check variant)
///   ZipPostLookup.GetAllByZip     ↔  Zip2City.GetAllCityStates
///   ZipPostLookup.GetClosest      ↔  Zip2City.GetClosestCityState
///   ZipPostLookup.GetByCity       ↔  (no equivalent — Zip2City is zip-only)
///   ZipPostLookup.GetByState      ↔  (no equivalent)
///   ZipPostLookup.GetByTimeZone   ↔  (no equivalent)
///
/// Zip2City only supports US zips; it has no city/state/timezone reverse-lookup.
///
/// US data: 57,400 rows — fully curated, all timezones and city names verified.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Config(typeof(BenchmarkConfig))]
public class LookupBenchmarks
{
    private ZipPostRegistry _postRegistry = null!;

    // A mix of zips spread across the keyspace to avoid branch-predictor and cache bias.
    // All verified zips from the curated US dataset.
    private static readonly string[] SampleZips =
    [
        "00501", "10001", "30301", "44101", "55101",
        "60601", "75201", "85001", "90210", "98101",
    ];

    private static readonly string[] MissingZips =
    [
        "00000", "11111", "22222", "33333", "44444",
    ];

    // Required by BenchmarkDotNet to properly consume deferred IEnumerable results
    private readonly Consumer _consumer = new();

    [GlobalSetup]
    public void Setup()
    {
        _postRegistry = new ZipPostRegistry(CountryCode.US);

        // Warm up Zip2City's static type initialiser so it pays no cost during measurement
        _ = GetDefaultCityState("10001");
    }

    // -------------------------------------------------------------------------
    // Single known-zip lookup (hit path)
    // -------------------------------------------------------------------------

    [Benchmark(Baseline = true, Description = "ZipPostLookup — GetByZip (hit)")]
    public CodeEntry? ZipPostLookup_GetByZip_Hit() =>
        _postRegistry.GetByZip("90210");

    [Benchmark(Description = "Zip2City — GetDefaultCityState (hit)")]
    public string[]? Zip2City_GetDefaultCityState_Hit() =>
        GetDefaultCityState("90210");

    // -------------------------------------------------------------------------
    // Miss path — zip that doesn't exist
    // -------------------------------------------------------------------------

    [Benchmark(Description = "ZipPostLookup — GetByZip (miss)")]
    public CodeEntry? ZipPostLookup_GetByZip_Miss() =>
        _postRegistry.GetByZip("00000");

    [Benchmark(Description = "Zip2City — GetDefaultCityState (miss)")]
    public string[]? Zip2City_GetDefaultCityState_Miss() =>
        GetDefaultCityState("00000");

    // -------------------------------------------------------------------------
    // All entries for a zip (multi-city codes)
    // -------------------------------------------------------------------------

    [Benchmark(Description = "ZipPostLookup — GetAllByZip (multi-city)")]
    public IReadOnlyList<CodeEntry> ZipPostLookup_GetAllByZip() =>
        _postRegistry.GetAllByZip("00603");   // Aguadilla + Ramey, Puerto Rico

    [Benchmark(Description = "Zip2City — GetAllCityStates")]
    public void Zip2City_GetAllCityStates() =>
        GetAllCityStates("00603").Consume(_consumer);

    // -------------------------------------------------------------------------
    // Closest zip
    // -------------------------------------------------------------------------

    [Benchmark(Description = "ZipPostLookup — GetClosest (no exact match)")]
    public CodeEntry? ZipPostLookup_GetClosest() =>
        _postRegistry.GetClosest("00001");

    [Benchmark(Description = "Zip2City — GetClosestCityState (no exact match)")]
    public string[]? Zip2City_GetClosestCityState() =>
        GetClosestCityState("00001");

    // -------------------------------------------------------------------------
    // Spread across multiple zips — more realistic than a single repeated key
    // -------------------------------------------------------------------------

    [Benchmark(Description = "ZipPostLookup — 10 GetByZip calls (spread)")]
    public int ZipPostLookup_GetByZip_Spread()
    {
        int found = 0;
        foreach (var zip in SampleZips)
        {
            if (_postRegistry.GetByZip(zip) != null) { found++; }
        }
        return found;
    }

    [Benchmark(Description = "Zip2City — 10 GetDefaultCityState calls (spread)")]
    public int Zip2City_GetDefaultCityState_Spread()
    {
        int found = 0;
        foreach (var zip in SampleZips)
        {
            if (GetDefaultCityState(zip) != null) { found++; }
        }
        return found;
    }

    // -------------------------------------------------------------------------
    // Miss spread
    // -------------------------------------------------------------------------

    [Benchmark(Description = "ZipPostLookup — 5 GetByZip calls (all miss)")]
    public int ZipPostLookup_GetByZip_MissSpread()
    {
        int found = 0;
        foreach (var zip in MissingZips)
        {
            if (_postRegistry.GetByZip(zip) != null) { found++; }
        }
        return found;
    }

    [Benchmark(Description = "Zip2City — 5 GetDefaultCityState calls (all miss)")]
    public int Zip2City_GetDefaultCityState_MissSpread()
    {
        int found = 0;
        foreach (var zip in MissingZips)
        {
            if (GetDefaultCityState(zip) != null) { found++; }
        }
        return found;
    }

    // -------------------------------------------------------------------------
    // Reverse lookups — ZipPostLookup only, no Zip2City equivalent
    // -------------------------------------------------------------------------

    [Benchmark(Description = "ZipPostLookup — GetByCity (no Zip2City equivalent)")]
    public IReadOnlyList<CodeEntry> ZipPostLookup_GetByCity() =>
        _postRegistry.GetByCity("New York");

    [Benchmark(Description = "ZipPostLookup — GetByState (no Zip2City equivalent)")]
    public IReadOnlyList<CodeEntry> ZipPostLookup_GetByState() =>
        _postRegistry.GetByState("NY");

    [Benchmark(Description = "ZipPostLookup — GetByTimeZone (no Zip2City equivalent)")]
    public IReadOnlyList<CodeEntry> ZipPostLookup_GetByTimeZone() =>
        _postRegistry.GetByTimeZone("America/New_York");
}