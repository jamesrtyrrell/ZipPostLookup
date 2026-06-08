using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using static ShereSoft.Zip2City;
using ZipPostLookup.Core;
using ZipPostLookup.ZPImage;

namespace ZipPostLookup.Benchmarks;

/// <summary>
/// Full CSV-vs-ZP-image comparison across all three built-in countries (US, CA, MX).
///
/// Backends compared for every operation:
///   • <see cref="ZipPostRegistry"/>   — the CSV-built registry (FrozenDictionary indexes)
///   • <see cref="ZpImageLookup"/>      — the binary ZP frozen image (MPHF + lazy materialisation)
///
/// GetByCode hit/miss also include a Zip2City column (US only) to complete the three-way picture.
/// For CA/MX params the Zip2City methods return null immediately (US-only library).
///
/// Layout:
///   • <see cref="Country"/> is a [Params] dimension — every benchmark runs once per country.
///   • Each operation is a [BenchmarkCategory] containing a CSV method (the baseline) and a ZPI
///     method. With ByCategory grouping the Ratio column reads as "ZPI relative to CSV" for the
///     same operation and country — the headline number this class exists to produce.
///
/// Construction / cold-load is deliberately NOT here — see <see cref="ConstructionBenchmarks"/>
/// (CSV build vs <c>ZpImageLookup.FromBuiltIn</c> load). GetClosest is US/MX-only (it requires a
/// 5-digit numeric code and throws for CA) so it lives in <see cref="ClosestImageBenchmarks"/>.
///
/// Datasets:
///   US — 57,400 entries (curated)
///   CA — 412,403 entries (FSA-compressed from 908,745 rows)
///   MX — 145,826 entries
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ZpImageBenchmarks
{
    // CountryCode is a readonly struct, so it cannot be a [Params] attribute argument
    // (attribute args must be compile-time constants). Param over the string code and rely on
    // the implicit string -> CountryCode conversion when building the backends.
    [Params("US", "CA", "MX")]
    public string Country = null!;

    private ZipPostRegistry _csv = null!;
    private ZpImageLookup   _zpi = null!;

    // Per-country probes, resolved in Setup.
    private string _hit = null!;        // exact fast-path code
    private string _hitAlt = null!;     // second code exercising the other path (range vs exact)
    private string _miss = null!;       // valid-format code not present in the data
    private string _multi = null!;      // code with >1 entry (resolved dynamically)
    private string _name = null!;       // place name for reverse lookup
    private string _stateCode = null!;  // admin1 code
    private string _stateName = null!;  // admin1 name
    private string _adminValue = null!; // admin level value for GetByAdmin
    private string _adminLevel = null!; // admin level name (State/Province/Estado)
    private string _tz = null!;         // IANA timezone

    private string[] _spread = null!;     // ~10 codes spanning the keyspace (mostly hits)
    private string[] _missSpread = null!; // ~5 codes that all miss
    private CodeBatch _batch = null!;     // pre-built batch over _spread

    [GlobalSetup]
    public void Setup()
    {
        switch (Country)
        {
            case "US":
                _hit = "90210"; _hitAlt = "00501"; _miss = "00000";
                _name = "New York"; _stateCode = "CA"; _stateName = "California";
                _adminValue = "California"; _adminLevel = "State"; _tz = "America/New_York";
                _spread =
                [
                    "00501", "10001", "30301", "44101", "55101",
                    "60601", "75201", "85001", "90210", "98101",
                ];
                _missSpread = ["00000", "99999", "00002", "11100", "98999"];
                break;

            case "CA":
                _hit = "M5V3L9"; _hitAlt = "T0A0A0"; _miss = "Z9Z9Z9";
                _name = "Toronto"; _stateCode = "ON"; _stateName = "Ontario";
                _adminValue = "Ontario"; _adminLevel = "Province"; _tz = "America/Toronto";
                _spread =
                [
                    "M5V3L9", "V6B1A1", "T0A0A0", "B2G1A1", "K1A0A1",
                    "H3T1A1", "S0G0A0", "A0G0A0", "T0B0A0", "G0R0A0",
                ];
                _missSpread = ["Z9Z9Z9", "X0X0X0", "Y1Y1Y1", "W2W2W2", "Z0Z0Z0"];
                break;

            case "MX":
                _hit = "20000"; _hitAlt = "64000"; _miss = "00000";
                _name = "Zona Centro"; _stateCode = "JAL"; _stateName = "Jalisco";
                _adminValue = "Jalisco"; _adminLevel = "Estado"; _tz = "America/Mexico_City";
                _spread =
                [
                    "20000", "64000", "22000", "44100", "06600",
                    "80000", "83000", "91000", "97000", "68000",
                ];
                _missSpread = ["00000", "99999", "00001", "11111", "09999"];
                break;

            default:
                throw new InvalidOperationException($"Unhandled country param '{Country}'.");
        }

        _csv = new ZipPostRegistry(Country);
        _zpi = ZpImageLookup.FromBuiltIn(Country);
        if (Country == "US") _ = GetDefaultCityState("10001"); // warm Zip2City static ctor

        // Resolve a genuinely multi-entry code from the data so GetAllByCode exercises the
        // multi-name path where it exists. Falls back to the primary hit if the country has none.
        _multi = _csv.GetAll()
            .GroupBy(e => e.ZpCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1)?.Key
            ?? _hit;

        _batch = new CodeBatch(_spread);

        // Parity guard: the two backends must agree before we measure either 
        AssertSame("hit", _csv.GetByCode(_hit), _zpi.GetByCode(_hit));
        AssertSame("hitAlt", _csv.GetByCode(_hitAlt), _zpi.GetByCode(_hitAlt));
        AssertSame("miss", _csv.GetByCode(_miss), _zpi.GetByCode(_miss));
        AssertCount("multi", _csv.GetAllByCode(_multi).Count, _zpi.GetAllByCode(_multi).Count);
        AssertCount("name", _csv.GetByName(_name).Count, _zpi.GetByName(_name).Count);
        AssertCount("state", _csv.GetByState(_stateCode).Count, _zpi.GetByState(_stateCode).Count);
        AssertCount("stateName", _csv.GetByStateName(_stateName).Count, _zpi.GetByStateName(_stateName).Count);
        AssertCount("admin", _csv.GetByAdmin(_adminValue, _adminLevel).Count, _zpi.GetByAdmin(_adminValue, _adminLevel).Count);
        AssertCount("tz", _csv.GetByTimeZone(_tz).Count, _zpi.GetByTimeZone(_tz).Count);
        // ── Warm the ZPI per-record cache so measured hits reflect steady state, not first touch ──
        foreach (var code in _spread) { _ = _zpi.GetByCode(code); _ = _csv.GetByCode(code); }
        _ = _zpi.GetAllByCode(_multi);
        _ = _zpi.GetByName(_name);
        _ = _zpi.GetBatch(_batch);
        _ = _csv.GetBatch(_batch);
    }

    private void AssertSame(string label, CodeEntry? a, CodeEntry? b)
    {
        if (a?.ZpCode != b?.ZpCode || a?.PlaceName != b?.PlaceName)
        {
            throw new InvalidOperationException(
                $"[{Country}] CSV/ZPI disagree on '{label}': " +
                $"'{a?.ZpCode}/{a?.PlaceName}' vs '{b?.ZpCode}/{b?.PlaceName}'.");
        }
    }

    private void AssertCount(string label, int a, int b)
    {
        if (a != b)
        {
            throw new InvalidOperationException(
                $"[{Country}] CSV/ZPI disagree on '{label}' count: {a} vs {b}.");
        }
    }

    // ── GetByCode hit ────────────────────────────────────────────────────────────────────────
    [BenchmarkCategory("GetByCode hit"), Benchmark(Baseline = true, Description = "CSV")]
    public CodeEntry? Csv_Hit() => _csv.GetByCode(_hit);

    [BenchmarkCategory("GetByCode hit"), Benchmark(Description = "ZPI")]
    public CodeEntry? Zpi_Hit() => _zpi.GetByCode(_hit);

    [BenchmarkCategory("GetByCode hit"), Benchmark(Description = "Zip2City")]
    public string[]? Zip2City_Hit() =>
        Country == "US" ? GetDefaultCityState(_hit) : null;

    // ── GetByCode alt path (CA: exact LDU vs range; US/MX: second exact code) ──────────────────
    [BenchmarkCategory("GetByCode alt"), Benchmark(Baseline = true, Description = "CSV")]
    public CodeEntry? Csv_HitAlt() => _csv.GetByCode(_hitAlt);

    [BenchmarkCategory("GetByCode alt"), Benchmark(Description = "ZPI")]
    public CodeEntry? Zpi_HitAlt() => _zpi.GetByCode(_hitAlt);

    // ── GetByCode miss ─────────────────────────────────────────────────────────────────────────
    [BenchmarkCategory("GetByCode miss"), Benchmark(Baseline = true, Description = "CSV")]
    public CodeEntry? Csv_Miss() => _csv.GetByCode(_miss);

    [BenchmarkCategory("GetByCode miss"), Benchmark(Description = "ZPI")]
    public CodeEntry? Zpi_Miss() => _zpi.GetByCode(_miss);

    [BenchmarkCategory("GetByCode miss"), Benchmark(Description = "Zip2City")]
    public string[]? Zip2City_Miss() =>
        Country == "US" ? GetDefaultCityState(_miss) : null;

    // ── GetAllByCode (multi-name) ────────────────────────────────────────────────────────────
    [BenchmarkCategory("GetAllByCode"), Benchmark(Baseline = true, Description = "CSV")]
    public IReadOnlyList<CodeEntry> Csv_GetAllByCode() => _csv.GetAllByCode(_multi);

    [BenchmarkCategory("GetAllByCode"), Benchmark(Description = "ZPI")]
    public IReadOnlyList<CodeEntry> Zpi_GetAllByCode() => _zpi.GetAllByCode(_multi);

    // ── GetByName ─────────────────────────────────────────────────────────────────────────────
    [BenchmarkCategory("GetByName"), Benchmark(Baseline = true, Description = "CSV")]
    public IReadOnlyList<CodeEntry> Csv_GetByName() => _csv.GetByName(_name);

    [BenchmarkCategory("GetByName"), Benchmark(Description = "ZPI")]
    public IReadOnlyList<CodeEntry> Zpi_GetByName() => _zpi.GetByName(_name);

    // ── GetByState (admin1 code) ──────────────────────────────────────────────────────────────
    [BenchmarkCategory("GetByState"), Benchmark(Baseline = true, Description = "CSV")]
    public IReadOnlyList<CodeEntry> Csv_GetByState() => _csv.GetByState(_stateCode);

    [BenchmarkCategory("GetByState"), Benchmark(Description = "ZPI")]
    public IReadOnlyList<CodeEntry> Zpi_GetByState() => _zpi.GetByState(_stateCode);

    // ── GetByStateName (admin1 name) ──────────────────────────────────────────────────────────
    [BenchmarkCategory("GetByStateName"), Benchmark(Baseline = true, Description = "CSV")]
    public IReadOnlyList<CodeEntry> Csv_GetByStateName() => _csv.GetByStateName(_stateName);

    [BenchmarkCategory("GetByStateName"), Benchmark(Description = "ZPI")]
    public IReadOnlyList<CodeEntry> Zpi_GetByStateName() => _zpi.GetByStateName(_stateName);

    // ── GetByAdmin (level name) ───────────────────────────────────────────────────────────────
    [BenchmarkCategory("GetByAdmin"), Benchmark(Baseline = true, Description = "CSV")]
    public IReadOnlyList<CodeEntry> Csv_GetByAdmin() => _csv.GetByAdmin(_adminValue, _adminLevel);

    [BenchmarkCategory("GetByAdmin"), Benchmark(Description = "ZPI")]
    public IReadOnlyList<CodeEntry> Zpi_GetByAdmin() => _zpi.GetByAdmin(_adminValue, _adminLevel);

    // ── GetByTimeZone ─────────────────────────────────────────────────────────────────────────
    [BenchmarkCategory("GetByTimeZone"), Benchmark(Baseline = true, Description = "CSV")]
    public IReadOnlyList<CodeEntry> Csv_GetByTimeZone() => _csv.GetByTimeZone(_tz);

    [BenchmarkCategory("GetByTimeZone"), Benchmark(Description = "ZPI")]
    public IReadOnlyList<CodeEntry> Zpi_GetByTimeZone() => _zpi.GetByTimeZone(_tz);

    // ── GetBatch (pre-built batch over the spread set) ────────────────────────────────────────
    [BenchmarkCategory("GetBatch"), Benchmark(Baseline = true, Description = "CSV")]
    public IReadOnlyDictionary<string, CodeEntry?> Csv_GetBatch() => _csv.GetBatch(_batch);

    [BenchmarkCategory("GetBatch"), Benchmark(Description = "ZPI")]
    public IReadOnlyDictionary<string, CodeEntry?> Zpi_GetBatch() => _zpi.GetBatch(_batch);

    // ── 10× GetByCode spread (mostly hits) ────────────────────────────────────────────────────
    [BenchmarkCategory("Spread (hit)"), Benchmark(Baseline = true, Description = "CSV")]
    public int Csv_Spread() => CountFound(_csv, _spread);

    [BenchmarkCategory("Spread (hit)"), Benchmark(Description = "ZPI")]
    public int Zpi_Spread() => CountFound(_zpi, _spread);

    // ── 5× GetByCode spread (all miss) ────────────────────────────────────────────────────────
    [BenchmarkCategory("Spread (miss)"), Benchmark(Baseline = true, Description = "CSV")]
    public int Csv_MissSpread() => CountFound(_csv, _missSpread);

    [BenchmarkCategory("Spread (miss)"), Benchmark(Description = "ZPI")]
    public int Zpi_MissSpread() => CountFound(_zpi, _missSpread);

    private static int CountFound(IZipPostLookup lookup, string[] codes)
    {
        var found = 0;
        foreach (var code in codes)
        {
            if (lookup.GetByCode(code) != null) { found++; }
        }

        return found;
    }
}
