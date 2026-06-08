using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using ZipPostLookup.Core;
using ZipPostLookup.ZPImage;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// CSV-vs-ZP-image comparison for <c>GetClosest</c>, which is valid only for countries with
/// 5-digit numeric codes. US and MX qualify; CA codes are alphanumeric and <c>GetClosest</c>
/// throws for them, so CA is excluded — this is why GetClosest is not part of the
/// <see cref="ZpImageBenchmarks"/> country matrix (a CA invocation there would abort the run).
///
/// The probe code is deliberately absent from the data so the binary-search nearest-match path
/// is exercised rather than an exact hit.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ClosestImageBenchmarks
{
    [Params("US", "MX")]
    public string Country = null!;

    private ZipPostRegistry _csv = null!;
    private ZpImageLookup   _zpi = null!;
    private string _probe = null!;

    [GlobalSetup]
    public void Setup()
    {
        // A valid 5-digit code with no exact match, to force the nearest-match search.
        _probe = "00001";

        _csv = new ZipPostRegistry(Country);
        _zpi = ZpImageLookup.FromBuiltIn(Country);

        var fromCsv = _csv.GetClosest(_probe);
        var fromZpi = _zpi.GetClosest(_probe);

        if (fromCsv?.ZpCode != fromZpi?.ZpCode)
        {
            throw new InvalidOperationException(
                $"[{Country}] CSV/ZPI disagree on GetClosest('{_probe}'): " +
                $"'{fromCsv?.ZpCode}' vs '{fromZpi?.ZpCode}'.");
        }
    }

    [BenchmarkCategory("GetClosest"), Benchmark(Baseline = true, Description = "CSV")]
    public CodeEntry? Csv_GetClosest() => _csv.GetClosest(_probe);

    [BenchmarkCategory("GetClosest"), Benchmark(Description = "ZPI")]
    public CodeEntry? Zpi_GetClosest() => _zpi.GetClosest(_probe);
}
