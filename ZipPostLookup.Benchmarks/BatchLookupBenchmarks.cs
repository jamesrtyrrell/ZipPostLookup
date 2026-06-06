using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ZipPostLookup.Core;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Batch lookup benchmarks — comparing <see>
///     <cref>ZipRegistry.GetBatch</cref>
/// </see>
/// against
/// equivalent sequential <see cref="ZipPostRegistry.GetByCode"/> calls.
/// 
/// Key questions:
///   1. Is batch faster than a loop of individual lookups?
///   2. How does batch scale with input size (10, 100, 1000 codes)?
///   3. What is the overhead of CodeBatch construction vs a raw List?
///   4. Does deduplication in CodeBatch pay off when duplicates are present?
/// 
/// US data: 57,400 entries — fully curated.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Config(typeof(BenchmarkConfig))]
public class BatchLookupBenchmarks
{
    private ZipPostRegistry _postRegistry = null!;

    // Pre-built batches — construction cost excluded from measurement
    private CodeBatch _batch10 = null!;
    private CodeBatch _batch100 = null!;
    private CodeBatch _batch1000 = null!;

    // Same codes as the batches, for sequential comparison
    private string[] _codes10 = null!;
    private string[] _codes100 = null!;
    private string[] _codes1000 = null!;

    // Codes with 50% duplicates — tests dedup benefit
    private CodeBatch _batchDupes = null!;
    private string[] _codesDupes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _postRegistry = new ZipPostRegistry(CountryCode.US);

        // Build sample code sets spread across the keyspace
        _codes10 = GenerateCodes(10);
        _codes100 = GenerateCodes(100);
        _codes1000 = GenerateCodes(1000);

        // Pre-build batches so construction isn't measured in lookup benchmarks
        _batch10 = new CodeBatch(_codes10);
        _batch100 = new CodeBatch(_codes100);
        _batch1000 = new CodeBatch(_codes1000);

        // 200 codes with 50% duplicates (100 unique) — tests dedup benefit
        var unique = GenerateCodes(100);
        _codesDupes = unique.Concat(unique).OrderBy(_ => Guid.NewGuid()).ToArray(); // shuffle
        _batchDupes = new CodeBatch(_codesDupes); // 100 unique after dedup
    }
    
    [Benchmark(Baseline = true, Description = "Sequential — 10 GetByCode calls")]
    public int Sequential_10()
    {
        int found = 0;
        foreach (var code in _codes10)
        {
            if (_postRegistry.GetByCode(code) != null) { found++; }
        }
        return found;
    }

    [Benchmark(Description = "Batch — 10 codes (CodeBatch)")]
    public IReadOnlyDictionary<string, CodeEntry?> Batch_10() =>
        _postRegistry.GetBatch(_batch10);

    [Benchmark(Description = "Batch — 10 codes (IEnumerable convenience overload)")]
    public IReadOnlyDictionary<string, CodeEntry?> Batch_10_Enumerable() =>
        _postRegistry.GetBatch(_codes10);

    // ── Batch vs sequential — 100 codes ──────────────────────────────────────

    [Benchmark(Description = "Sequential — 100 GetByCode calls")]
    public int Sequential_100()
    {
        int found = 0;
        foreach (var code in _codes100)
        {
            if (_postRegistry.GetByCode(code) != null) { found++; }
        }
        return found;
    }

    [Benchmark(Description = "Batch — 100 codes (CodeBatch)")]
    public IReadOnlyDictionary<string, CodeEntry?> Batch_100() =>
        _postRegistry.GetBatch(_batch100);

    // ── Batch vs sequential — 1000 codes ─────────────────────────────────────

    [Benchmark(Description = "Sequential — 1000 GetByCode calls")]
    public int Sequential_1000()
    {
        int found = 0;
        foreach (var code in _codes1000)
        {
            if (_postRegistry.GetByCode(code) != null) { found++; }
        }
        return found;
    }

    [Benchmark(Description = "Batch — 1000 codes (CodeBatch)")]
    public IReadOnlyDictionary<string, CodeEntry?> Batch_1000() =>
        _postRegistry.GetBatch(_batch1000);

    // ── Deduplication benefit ─────────────────────────────────────────────────

    [Benchmark(Description = "Sequential — 200 codes with 50% duplicates (redundant work)")]
    public int Sequential_WithDupes()
    {
        int found = 0;
        foreach (var code in _codesDupes)
        {
            if (_postRegistry.GetByCode(code) != null) { found++; }
        }
        return found;
    }

    [Benchmark(Description = "Batch — 200 codes with 50% duplicates (100 unique lookups)")]
    public IReadOnlyDictionary<string, CodeEntry?> Batch_WithDupes() =>
        _postRegistry.GetBatch(_batchDupes);

    // ── CodeBatch construction cost ────────────────────────────────────────

    [Benchmark(Description = "CodeBatch construction — 100 codes (single-threaded)")]
    public CodeBatch BatchConstruct_100_SingleThreaded() =>
        new CodeBatch(_codes100);

    [Benchmark(Description = "CodeBatch construction — 100 codes (concurrent)")]
    public CodeBatch BatchConstruct_100_Concurrent() =>
        new CodeBatch(_codes100, concurrent: true);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string[] GenerateCodes(int count)
    {
        // Generate codes spread evenly across the US 5-digit zip range
        // Not all will exist in the registry — realistic miss rate included
        var codes = new string[count];
        var step = 99999 / count;

        for (int i = 0; i < count; i++)
        {
            codes[i] = (step * i + 1).ToString("D5");
        }

        return codes;
    }
}