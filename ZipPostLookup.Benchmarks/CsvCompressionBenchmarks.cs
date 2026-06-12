using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ZipPostLookup.Core;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Measures the overhead of Brotli decompression on the CSV stream-reading path
/// introduced in the net8+ package for 1.0.0.
///
/// Before 1.0.0: ZipPostRegistry loaded from plaintext embedded CSVs (~28.4 MB total).
/// From  1.0.0:  ZipPostRegistry loads from Brotli-compressed embedded CSVs (~1.9 MB total).
///
/// Both paths use identical CSV parsing logic. The only variable is whether the
/// stream is a plain MemoryStream (old path) or a BrotliStream wrapping a MemoryStream
/// (new path). FrozenDictionary index construction is deliberately excluded — it dominates
/// overall ZipPostRegistry construction time and would swamp the decompression signal.
/// See <see cref="ConstructionBenchmarks"/> for the end-to-end cold-start cost.
///
/// Setup: the embedded .csv.br bytes are loaded once from the library assembly; the
/// corresponding plain CSV bytes are produced by decompressing once in GlobalSetup.
/// Both remain in memory for the duration of the run — no filesystem I/O during benchmarks.
///
/// Compressed sizes (Brotli SmallestSize; live file sizes — keep in sync after every export):
///   US — 2,020 KB → 281 KB  (7×)
///   CA — 20,878 KB → 783 KB (27×)
///   MX — 5,837 KB → 852 KB  (7×)
/// </summary>
[MemoryDiagnoser]
// Each invocation decompresses/reads a multi-MB CSV stream (low-ms, allocation-heavy). Pin
// counts so the adaptive engine doesn't run up toward MaxIterationCount chasing a stable mean.
[SimpleJob(RuntimeMoniker.Net80, launchCount: 1, warmupCount: 3, iterationCount: 8)]
public class CsvCompressionBenchmarks
{
    private byte[] _usBr  = null!;
    private byte[] _caBr  = null!;
    private byte[] _mxBr  = null!;
    private byte[] _usCsv = null!;
    private byte[] _caCsv = null!;
    private byte[] _mxCsv = null!;

    [GlobalSetup]
    public void Setup()
    {
        _usBr  = LoadBrBytes("us");
        _caBr  = LoadBrBytes("ca");
        _mxBr  = LoadBrBytes("mx");
        _usCsv = Decompress(_usBr);
        _caCsv = Decompress(_caBr);
        _mxCsv = Decompress(_mxBr);
    }

    // ── Brotli path (1.0.0+ — reads from compressed embedded resource) ────────

    [Benchmark(Baseline = true, Description = "Brotli stream — US (281 KB → 2,020 KB)")]
    public int Brotli_Us() => CountLines(_usBr, brotli: true);

    [Benchmark(Description = "Brotli stream — CA (783 KB → 20,878 KB)")]
    public int Brotli_Ca() => CountLines(_caBr, brotli: true);

    [Benchmark(Description = "Brotli stream — MX (852 KB → 5,837 KB)")]
    public int Brotli_Mx() => CountLines(_mxBr, brotli: true);

    // ── Plain stream (1.0.0-rc.1 and earlier — reads raw bytes, no decompression) ─

    [Benchmark(Description = "Plain stream  — US (2,020 KB)")]
    public int Plain_Us() => CountLines(_usCsv, brotli: false);

    [Benchmark(Description = "Plain stream  — CA (20,878 KB)")]
    public int Plain_Ca() => CountLines(_caCsv, brotli: false);

    [Benchmark(Description = "Plain stream  — MX (5,837 KB)")]
    public int Plain_Mx() => CountLines(_mxCsv, brotli: false);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads all lines from a stream backed by <paramref name="bytes"/> and returns
    /// the line count to prevent dead-code elimination. When <paramref name="brotli"/>
    /// is true the bytes are routed through a BrotliStream before the StreamReader.
    /// </summary>
    private static int CountLines(byte[] bytes, bool brotli)
    {
        var ms = new MemoryStream(bytes, writable: false);
        Stream csvStream = brotli
            ? new BrotliStream(ms, CompressionMode.Decompress)
            : ms;

        using var reader = new StreamReader(csvStream);

        var count = 0;
        while (reader.ReadLine() != null)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Loads the embedded <c>{cc}.csv.br</c> resource from the library assembly into a
    /// byte array. Called once in GlobalSetup; not part of any timed benchmark path.
    /// </summary>
    private static byte[] LoadBrBytes(string cc)
    {
        var assembly = typeof(ZipPostRegistry).Assembly;
        var name     = assembly
            .GetManifestResourceNames()
            .First(n => n.EndsWith($"{cc}.csv.br", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var ms     = new MemoryStream((int)stream.Length);
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Produces plain CSV bytes by decompressing <paramref name="brBytes"/> once.
    /// Called once per country in GlobalSetup — this cost is NOT measured.
    /// </summary>
    private static byte[] Decompress(byte[] brBytes)
    {
        using var input  = new MemoryStream(brBytes, writable: false);
        using var decomp = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decomp.CopyTo(output);
        return output.ToArray();
    }
}
