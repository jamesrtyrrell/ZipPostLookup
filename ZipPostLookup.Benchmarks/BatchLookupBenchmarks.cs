using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ZipPostLookup.Core;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Batch lookup benchmarks — <see cref="ZipPostRegistry.GetBatch"/> vs equivalent sequential
/// <see cref="ZipPostRegistry.GetByCode"/> calls at three input sizes.
///
/// Key questions:
///   1. Is batch faster than a loop of individual lookups?
///   2. How does the gap scale with input size (10, 100, 1000 codes)?
///
/// US data: 57,400 entries — fully curated.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Config(typeof(BenchmarkConfig))]
public class BatchLookupBenchmarks
{
    private ZipPostRegistry _postRegistry = null!;

    private CodeBatch _batch10   = null!;
    private CodeBatch _batch100  = null!;
    private CodeBatch _batch1000 = null!;

    private string[] _codes10   = null!;
    private string[] _codes100  = null!;
    private string[] _codes1000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _postRegistry = new ZipPostRegistry(CountryCode.US);

        _codes10   = GenerateCodes(10);
        _codes100  = GenerateCodes(100);
        _codes1000 = GenerateCodes(1000);

        _batch10   = new CodeBatch(_codes10);
        _batch100  = new CodeBatch(_codes100);
        _batch1000 = new CodeBatch(_codes1000);
    }

    // ── 10 codes ──────────────────────────────────────────────────────────────

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

    // ── 100 codes ─────────────────────────────────────────────────────────────

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

    // ── 1000 codes ────────────────────────────────────────────────────────────

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string[] GenerateCodes(int count)
    {
        // Evenly spread across the US 5-digit zip range; not all exist (realistic miss rate)
        var codes = new string[count];
        var step  = 99999 / count;

        for (int i = 0; i < count; i++)
            codes[i] = (step * i + 1).ToString("D5");

        return codes;
    }
}
