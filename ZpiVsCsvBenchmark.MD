# ZipPostLookup — CSV vs ZP Frozen Image Benchmark

Head-to-head comparison of the two lookup backends across all three built-in countries:

- **CSV** — `ZipPostRegistry`, built by parsing the embedded `{cc}.csv` into `FrozenDictionary` indexes.
- **ZPI** — `ZpImageLookup`, loaded from the embedded `{cc}.zpi.br` binary frozen image (minimal perfect hash + lazy `CodeEntry` materialisation).

> **Status:** ✅ **measured (2026-06-12).** Results below use the v4 ZPI format (fused directory + lazy NamePool, atop the fastrange MPHF + single-pass FNV reader). CA data includes fully-populated admin data (603k FSA-compressed entries, 25 timezones). This run also reflects the **2026-06-12 fix to a quadratic insert in CSV registry construction** (`AddToIndex`), which cut the CA build from ~33 s to ~1.4 s and reshaped the cold-start comparison in §3.3. Numbers are from an i7-13700K (see §2).

---

## 1. How to run

All benchmarks are net8.0 and **must** be run in Release.

```bash
# Full CSV-vs-ZPI comparison — lookup matrix (US/CA/MX) + GetClosest (US/MX) + construction/load
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*ZpImageBenchmarks*" "*ClosestImageBenchmarks*" "*ConstructionBenchmarks*"

# Just the lookup matrix (fast — no heavy CA construction)
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*ZpImageBenchmarks*"

# Restrict to one or more operation categories (categories are the operation names)
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*ZpImageBenchmarks*" --anyCategories "GetByCode hit" "GetByCode miss"
```

> All three benchmark classes use the `Country` `[Params]` dimension, so every run already covers
> US/CA/MX; there is no single-country filter — narrow by **operation** with `--anyCategories` instead.

Artifacts (the GitHub-flavoured markdown tables to paste below) land in:

```
ZipPostLookup.Benchmarks/BenchmarkDotNet.Artifacts/results/
  ZipPostLookup.Benchmarks.ZpImageBenchmarks-report-github.md
  ZipPostLookup.Benchmarks.ClosestImageBenchmarks-report-github.md
  ZipPostLookup.Benchmarks.ConstructionBenchmarks-report-github.md
```

> **Note on the run cost:** `ConstructionBenchmarks` builds the CA CSV registry (~1.4 s / ~817 MB per
> build) many times. Since the 2026-06-12 construction fix this is far cheaper than it used to be
> (~33 s/build before), but it is still the slowest class. The lookup matrix itself is fast. Run the
> matrix alone while iterating, and the full set when you want final cold-start numbers.

### Reading the output

`ZpImageBenchmarks` and `ClosestImageBenchmarks` group by `{Country × operation}` category, with the
**CSV method as the baseline** of each group. So the `Ratio` column reads directly as *"ZPI relative
to CSV for this operation in this country"* — `Ratio < 1.00` means ZPI is faster, `> 1.00` slower.
`Allocated` and `Ratio` of allocations tell the zero-allocation story per operation.

---

## 2. Environment

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.22631.6199/23H2)
13th Gen Intel Core i7-13700K 3.40GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 8.0.28 (8.0.28, 8.0.2826.26413), X64 RyuJIT x86-64-v3
  .NET 8.0 : .NET 8.0.28 (8.0.28, 8.0.2826.26413), X64 RyuJIT x86-64-v3
```

Embedded resource sizes (the on-disk cost of each backend's data). The library now embeds the
**Brotli-compressed `{cc}.csv.br`** for the CSV backend (not the plain `.csv`), plus the
`{cc}.zpi.br` image — so the honest disk comparison is `.csv.br` vs `.zpi.br`:

| Country | `{cc}.csv` (decompressed) | `{cc}.csv.br` (embedded) | `{cc}.zpi.br` (embedded) | Image vs csv.br |
|---------|--------------------------:|-------------------------:|-------------------------:|----------------:|
| US      |                  2,020 KB |               **282 KB** |                 **727 KB** |          2.58×  |
| MX      |                  5,837 KB |               **852 KB** |               **1,292 KB** |          1.52×  |
| CA      |                 20,878 KB |               **784 KB** |               **5,882 KB** |          7.51×  |

> **Disk-size note (changed).** Because the CSV is now shipped Brotli-compressed, the `.zpi.br`
> image is no longer the smallest artifact on disk — plain CSV text compresses extremely well
> (CA: 20.9 MB → 784 KB). The image's advantage is **load time and startup memory** (§3.3), not
> disk footprint. The package ships both backends, so on-disk cost is `.csv.br` + `.zpi.br`.

Datasets under test: **US** 43,498 distinct codes (59,719 rows) · **CA** ~900,686 distinct codes (603,272 FSA-compressed entries) · **MX** 32,893 distinct codes (146,183 rows).

---

## 3. Results

`Ratio` = ZPI relative to CSV (baseline = 1.00). **< 1.00 → ZPI faster.** Allocated is per call.

### 3.1 Lookup matrix — `ZpImageBenchmarks` (CSV baseline vs ZPI, per country)

Results from the v4 ZPI format (fused directory + lazy NamePool; run 2026-06-12).

| Operation | Country | CSV Mean | ZPI Mean | Ratio | CSV alloc | ZPI alloc |
|---|---|--:|--:|--:|--:|--:|
| **GetByCode** hit | US | 6.16 ns | 8.13 ns | 1.32× | 0 B | 0 B |
| | MX | 5.78 ns | 7.95 ns | 1.38× | 0 B | 0 B |
| | CA | 6.99 ns | 9.00 ns | 1.29× | 0 B | 0 B |
| **GetByCode** alt ¹ | US | 6.98 ns | 8.25 ns | 1.18× | 0 B | 0 B |
| | MX | 5.79 ns | 8.62 ns | 1.49× | 0 B | 0 B |
| | CA | 6.83 ns | 9.63 ns | 1.41× | 0 B | 0 B |
| **GetByCode** miss | US | 30.46 ns | 24.20 ns | **0.79×** | 104 B | **64 B** |
| | MX | 33.46 ns | 24.81 ns | **0.74×** | 104 B | **64 B** |
| | CA | 37.80 ns | 32.20 ns | **0.85×** | 104 B | **64 B** |
| **GetAllByCode** | US | 8.96 ns | 14.12 ns | 1.58× | 24 B | 40 B |
| | MX | 8.74 ns | 12.87 ns | 1.47× | 24 B | 40 B |
| | CA | 8.87 ns | 15.05 ns | 1.70× | 24 B | 40 B |
| **GetByName** | US | 11.29 ns | 15.52 ns | 1.38× | 24 B | **0 B** |
| | MX | 14.37 ns | 17.92 ns | 1.25× | 24 B | **0 B** |
| | CA | 11.19 ns | 15.14 ns | 1.35× | 24 B | **0 B** |
| **GetByState** | US | 6.05 ns | 7.93 ns | 1.31× | 24 B | **0 B** |
| | MX | 6.91 ns | 9.01 ns | 1.31× | 24 B | **0 B** |
| | CA | 6.99 ns | 9.08 ns | 1.30× | 24 B | **0 B** |
| **GetByStateName** | US | 9.55 ns | 11.23 ns | 1.18× | 24 B | **0 B** |
| | MX | 9.96 ns | 10.38 ns | **1.04×** | 24 B | **0 B** |
| | CA | 6.23 ns | 9.54 ns | 1.53× | 24 B | **0 B** |
| **GetByAdmin** | US | 17.73 ns | 16.93 ns | **0.95×** | 80 B | **0 B** |
| | MX | 16.46 ns | 17.42 ns | 1.06× | 80 B | **0 B** |
| | CA | 14.01 ns | 17.97 ns | 1.28× | 80 B | **0 B** |
| **GetByTimeZone** | US | 8.41 ns | 10.59 ns | 1.26× | 24 B | **0 B** |
| | MX | 7.63 ns | 11.71 ns | 1.54× | 24 B | **0 B** |
| | CA | 7.73 ns | 11.68 ns | 1.51× | 24 B | **0 B** |
| **GetBatch** | US | 168.4 ns | 212.9 ns | 1.26× | 480 B | 480 B |
| | MX | 173.4 ns | 212.1 ns | 1.22× | 480 B | 480 B |
| | CA | 295.9 ns | 313.4 ns | 1.06× | 856 B | **672 B** |
| **Spread (hit)** 10× | US | 66.1 ns | 92.0 ns | 1.39× | 0 B | 0 B |
| | MX | 64.2 ns | 91.5 ns | 1.43× | 0 B | 0 B |
| | CA | 199.1 ns | 207.5 ns | 1.04× | 376 B | **192 B** |
| **Spread (miss)** 5× | US | 168.6 ns | 138.4 ns | **0.82×** | 520 B | **320 B** |
| | MX | 149.5 ns | 133.6 ns | **0.89×** | 416 B | **256 B** |
| | CA | 172.7 ns | 195.4 ns | 1.13× | 520 B | **320 B** |

¹ CA's `GetByCode alt` probe is `T0A0A0` — an exact LDU in a mixed FSA (no range involved).
  CA's range-resolved hit (primary probe `M5V3L9`) is the `GetByCode hit` row and costs the same
  ~6.99 ns as an exact hit, confirming range resolution adds no observable overhead on the hot path.

**Reading it (v4 format):** ZPI exact hits are **1.18–1.49× CSV** (single-digit ns, zero-alloc).
ZPI **beats CSV on the miss path** (0.74–0.85×) and allocates 38% less (64 B vs 104 B). Several
reverse lookups are **at or below CSV cost** (GetByAdmin US 0.95×, GetByStateName MX 1.04×, Spread
miss US/MX 0.82/0.89×) and **every reverse lookup is allocation-free on ZPI** (0 B vs CSV's 24–80 B).
The widest gap is `GetAllByCode` (1.47–1.70×, the only reverse op where ZPI allocates) and the hit
`Spread` (10× hits compound the per-probe constant). See §3.4 for the reverse-lookup caching story.

### 3.2 GetClosest — `ClosestImageBenchmarks` (US, MX)

| Country | CSV Mean | ZPI Mean | Ratio | Allocated |
|---|--:|--:|--:|--:|
| US | 44.16 ns | 44.10 ns | **1.00** | 32 B (both) |
| MX | 34.39 ns | 41.10 ns | 1.19 | 32 B (both) |

### 3.3 Construction / cold load — `ConstructionBenchmarks`

The image's headline win: Brotli-decompress-and-map (no per-row construction) vs CSV parse-and-index.

| Country | CSV build | ZPI load | Speedup | CSV alloc | ZPI alloc |
|---|--:|--:|--:|--:|--:|
| US (60k rows)   |    136 ms  | **7.8 ms** | **17×**  |   81.1 MB |  2.8 MB |
| MX (146k rows)  |    375 ms  |**14.3 ms** | **26×**  |  189.3 MB |  6.3 MB |
| CA (603k rows)  |  1,373 ms  |**55.2 ms** | **25×**  |  816.6 MB | 26.3 MB |
| US+CA+MX        |  2,186 ms  | **~77 ms** | **~28×** | 1,073  MB | ~35 MB  |

(Zip2City baseline — compile-time-baked data — initialises in ~0.03 ms / 160 B.)

> **Big change since prior runs.** Before the 2026-06-12 `AddToIndex` fix, the CA CSV build was
> ~33 s and the image loaded it ~340–430× faster. That quadratic insert is gone — CA now builds in
> ~1.4 s — so the image's **time** advantage has collapsed from hundreds of× to ~17–28×. Its
> **memory** advantage actually grew: ZPI load now allocates **~29–31× less** than the CSV build
> (lazy NamePool keeps untouched names off the heap at load). The image remains the right choice
> for cold-start-sensitive and memory-constrained deployments; the CSV path is no longer painful.

### 3.4 Reverse-lookup result caching — before vs after (CA, the worst case)

The reverse lookups originally re-materialised the entire result set into a fresh array on every call
(landing CA's large sets on the large-object heap, triggering Gen2 GC). Per-bucket result caching now
returns the built list reference on repeat calls, exactly like the CSV registry:

| Operation (CA) | Before (no cache) | After (cached, 2026-06-12) | Speedup | Alloc before → after |
|---|--:|--:|--:|--:|
| GetByState     | 1,135,415 ns |  **9.08 ns** | ~125,000× | 687 KB → **0 B** |
| GetByStateName |   895,587 ns |  **9.54 ns** |  ~94,000× | 687 KB → **0 B** |
| GetByTimeZone  |   976,154 ns | **11.68 ns** |  ~84,000× | 1.38 MB → **0 B** |
| GetByAdmin     |   509,872 ns | **17.97 ns** |  ~28,000× | 687 KB → **0 B** |
| GetByName      |     7,670 ns | **15.14 ns** |    ~510×  | 51 KB → **0 B** |

Two reader-side fixes delivered this (no on-disk format change): a lazy `record→directory-slot` map
(`CodeForExactRecord` O(1) instead of O(n) per record) and the per-bucket / per-name result caches
(`_tzPostingCache`, `_adminPostingCache`, `_nameResultCache`). Correctness is guarded by
`ZpImageParityTests` / `CaZpImageParityTests` (9/9 pass).

### 3.5 Verdict

ZPI's two real wins are now **startup memory** and **load time**: it loads **17–28× faster** than
building the CSV registry and allocates **~29–31× less** at load — and (with the v4 format) it matches
or beats CSV on most query categories (exact hits **1.18–1.49× CSV** at 8–10 ns, zero-alloc; miss path
**ZPI faster: 0.74–0.85×** with 38% less allocation; reverse lookups **0.95–1.70×** and **all
allocation-free**). The remaining per-lookup overhead is a small constant (MPHF probe + bucket
resolution) that doesn't scale with dataset size.

Two things changed the calculus versus earlier runs. First, the **2026-06-12 construction fix** cut the
CA CSV build from ~33 s to ~1.4 s, so the image's *time* lead shrank from hundreds-of-× to ~17–28× (the
memory lead grew). Second, the library now embeds the **Brotli-compressed `.csv.br`**, which compresses
far better than the binary image — so ZPI is **no longer the smallest artifact on disk** (§2). Its
value is load/runtime, not footprint.

**Recommendation:** the frozen image remains a strong choice for cold-start-sensitive or
memory-constrained deployments (Lambda, CLIs, large CA datasets), where ~30× less startup allocation
and a one-decompress load beat the CSV build. For an always-warm service where `GetAllByCode` volume
dominates and that ~1.5–1.7× gap matters more than zero-alloc reverse lookups, the CSV registry's
`FrozenDictionary` hot path may still edge it. Both remain interchangeable behind `IZipPostLookup`.

---

## 4. Benchmark source

| File | Class | What it measures |
|------|-------|------------------|
| `ZpImageBenchmarks.cs` | `ZpImageBenchmarks` | CSV vs ZPI lookup matrix, `[Params]` over US/CA/MX |
| `ClosestImageBenchmarks.cs` | `ClosestImageBenchmarks` | CSV vs ZPI `GetClosest`, US/MX (CA excluded — alphanumeric codes) |
| `ConstructionBenchmarks.cs` | `ConstructionBenchmarks` | CSV build vs ZPI load (+ Zip2City baseline), all countries |
| `MultiCountryBenchmarks.cs` | `MultiCountryBenchmarks` | Combined US+CA+MX registry: per-country lookup + CA range resolve + cross-country timezone |
| `LegacyVsModernBenchmarks.cs` | `LegacyVsModernBenchmarks` | `Dictionary` (netstandard2.0) vs `FrozenDictionary` (net8.0) hot path |
| `CsvCompressionBenchmarks.cs` | `CsvCompressionBenchmarks` | Brotli vs plain CSV stream read (the I/O share of construction) |
| `BatchLookupBenchmarks.cs` | `BatchLookupBenchmarks` | `GetBatch` vs sequential loop at 10/100/1000 codes; dedup benefit |
| `LookupBenchmarks.cs` | `LookupBenchmarks` | US hot-path vs Zip2City; `GetClosest`; reverse lookups |

Each lookup operation is a `[BenchmarkCategory]` containing a CSV method (`[Benchmark(Baseline = true)]`)
and a ZPI method, with `GroupBenchmarksBy(ByCategory)` so the `Ratio` column is per-operation,
per-country. `[GlobalSetup]` runs a CSV-vs-ZPI **parity guard** before measuring, so the comparison
fails fast if the two backends ever disagree on a probed result.
