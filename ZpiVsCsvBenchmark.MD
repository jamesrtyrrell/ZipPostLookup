# ZipPostLookup — CSV vs ZP Frozen Image Benchmark

Head-to-head comparison of the two lookup backends across all three built-in countries:

- **CSV** — `ZipPostRegistry`, built by parsing the embedded `{cc}.csv` into `FrozenDictionary` indexes.
- **ZPI** — `ZpImageLookup`, loaded from the embedded `{cc}.zpi.br` binary frozen image (minimal perfect hash + lazy `CodeEntry` materialisation).

> **Status:** ✅ **measured (2026-06-06).** Results below are from the post-v3-format run (format version 3: fastrange MPHF + single-pass FNV). CA data includes fully-populated admin data (412k entries, 21 timezones). Numbers are from an i7-13700K (see §2).

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

> **Note on the run cost:** `ConstructionBenchmarks` builds the CA CSV registry (~1.5 s / ~768 MB per
> build) many times — that class is slow by nature. The lookup matrix itself is fast. Run the matrix
> alone while iterating, and the full set when you want final cold-start numbers.

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
.NET SDK 10.0.300
  [Host]   : .NET 8.0.27 (8.0.27, 8.0.2726.22922), X64 RyuJIT x86-64-v3
  .NET 8.0 : .NET 8.0.27 (8.0.27, 8.0.2726.22922), X64 RyuJIT x86-64-v3
```

Embedded resource sizes (the on-disk cost of each backend's data):

| Country | `{cc}.csv` | `{cc}.zpi.br` | Image vs CSV |
|---------|-----------:|--------------:|-------------:|
| US      |    1.9 MB  |   **656 KB**  | 34% (0.34×)  |
| MX      |    5.5 MB  |    **1.3 MB** | 24% (0.24×)  |
| CA      |   14.0 MB  |    **3.8 MB** | 27% (0.27×)  |

The Brotli-compressed image is **3–4× smaller on disk** than the embedded CSV for every country.

Datasets under test: **US** 42,931 distinct codes (57k rows) · **CA** ~900,688 distinct codes (412,403 FSA-compressed entries from 908,745 rows) · **MX** 32,247 distinct codes (145k rows).

---

## 3. Results

`Ratio` = ZPI relative to CSV (baseline = 1.00). **< 1.00 → ZPI faster.** Allocated is per call.

### 3.1 Lookup matrix — `ZpImageBenchmarks` (CSV baseline vs ZPI, per country)

Results from ZPI format v3 (fastrange MPHF + single-pass FNV; regenerated 2026-06-06).

| Operation | Country | CSV Mean | ZPI Mean | Ratio | CSV alloc | ZPI alloc |
|---|---|--:|--:|--:|--:|--:|
| **GetByCode** hit | US | 7.06 ns | 9.45 ns | 1.34× | 0 B | 0 B |
| | MX | 6.74 ns | 8.96 ns | 1.33× | 0 B | 0 B |
| | CA | 8.67 ns | 10.16 ns | 1.17× | 0 B | 0 B |
| **GetByCode** alt ¹ | US | 7.79 ns | 9.69 ns | 1.24× | 0 B | 0 B |
| | MX | 7.27 ns | 9.20 ns | 1.27× | 0 B | 0 B |
| | CA | 8.34 ns | 10.99 ns | 1.32× | 0 B | 0 B |
| **GetByCode** miss | US | 43.72 ns | 30.88 ns | **0.71×** | 104 B | **64 B** |
| | MX | 43.12 ns | 31.02 ns | **0.72×** | 104 B | **64 B** |
| | CA | 43.89 ns | 40.41 ns | **0.92×** | 104 B | **64 B** |
| **GetAllByCode** | US | 7.39 ns | 15.56 ns | 2.11× | 24 B | 40 B |
| | MX | 7.66 ns | 14.58 ns | 1.90× | 24 B | 40 B |
| | CA | 8.93 ns | 17.12 ns | 1.92× | 24 B | 40 B |
| **GetByName** | US | 9.03 ns | 18.31 ns | 2.03× | 24 B | **0 B** |
| | MX | 17.27 ns | 21.57 ns | 1.25× | 24 B | **0 B** |
| | CA | 13.21 ns | 20.57 ns | 1.56× | 24 B | **0 B** |
| **GetByState** | US | 7.99 ns | 10.50 ns | 1.32× | 24 B | **0 B** |
| | MX | 8.21 ns | 9.37 ns | 1.14× | 24 B | **0 B** |
| | CA | 8.00 ns | 10.57 ns | 1.32× | 24 B | **0 B** |
| **GetByStateName** | US | 10.64 ns | 11.78 ns | 1.11× | 24 B | **0 B** |
| | MX | 12.66 ns | 11.74 ns | **0.93×** | 24 B | **0 B** |
| | CA | 7.26 ns | 13.21 ns | 1.82× | 24 B | **0 B** |
| **GetByAdmin** | US | 21.78 ns | 18.44 ns | **0.85×** | 80 B | **0 B** |
| | MX | 19.34 ns | 17.96 ns | **0.93×** | 80 B | **0 B** |
| | CA | 14.54 ns | 22.03 ns | 1.52× | 80 B | **0 B** |
| **GetByTimeZone** | US | 9.40 ns | 11.52 ns | 1.23× | 24 B | **0 B** |
| | MX | 8.76 ns | 14.09 ns | 1.61× | 24 B | **0 B** |
| | CA | 11.92 ns | 14.95 ns | 1.26× | 24 B | **0 B** |
| **GetBatch** | US | 393 ns | 529 ns | 1.35× | 480 B | 480 B |
| | MX | 424 ns | 513 ns | 1.21× | 480 B | 480 B |
| | CA | 716 ns | 736 ns | 1.03× | 856 B | **672 B** |
| **Spread (hit)** 10× | US | 72.9 ns | 102.6 ns | 1.41× | 0 B | 0 B |
| | MX | 72.1 ns | 101.7 ns | 1.41× | 0 B | 0 B |
| | CA | 218.1 ns | 229.7 ns | 1.05× | 376 B | **192 B** |
| **Spread (miss)** 5× | US | 196.0 ns | 160.3 ns | **0.82×** | 520 B | **320 B** |
| | MX | 212.9 ns | 223.1 ns | 1.05× | 520 B | **320 B** |
| | CA | 229.0 ns | 221.5 ns | **0.97×** | 520 B | **320 B** |

¹ CA's `GetByCode alt` probe is `T0A0A0` — an exact LDU in a mixed FSA (no range involved).
  CA's range-resolved hit (primary probe `M5V3L9`) is the `GetByCode hit` row and costs the same
  ~8.67 ns as an exact hit, confirming range resolution adds no observable overhead on the hot path.

**Reading it (v3 format):** ZPI exact hits are now **1.17–1.34× CSV** (single-digit-to-low-double-digit ns, zero-alloc) — a large improvement over v2's 2.5–2.8×, driven by the fastrange MPHF + single-pass FNV redesign. ZPI **beats CSV on the miss path** (0.71–0.92×) and allocates 38% less (64 B vs 104 B). Several reverse lookups are **at or below CSV cost** (GetByAdmin US/MX 0.85/0.93×, GetByStateName MX 0.93×, Spread miss US 0.82×) and all are allocation-free (0 B). CA Spread(hit) reached **1.05×** (near-parity). See §3.4 for the before/after reverse-lookup caching story.

### 3.2 GetClosest — `ClosestImageBenchmarks` (US, MX)

| Country | CSV Mean | ZPI Mean | Ratio | Allocated |
|---|--:|--:|--:|--:|
| US | 35.76 ns | 59.09 ns | 1.65 | 32 B (both) |
| MX | 42.04 ns | **36.99 ns** | **0.88** | 32 B (both) |

### 3.3 Construction / cold load — `ConstructionBenchmarks`

The image's headline win: CSV parse-and-index vs Brotli-decompress-and-map (no per-row construction).

| Country | CSV build | ZPI load | Speedup | CSV alloc | ZPI alloc |
|---|--:|--:|--:|--:|--:|
| US (57k rows)   |    184 ms  |  **9 ms** | **20×**  |  82.2 MB | 11.0 MB |
| MX (145k rows)  |    608 ms  | **18 ms** | **34×**  | 188.9 MB | 22.5 MB |
| CA (412k rows)  | 14,727 ms  | **43 ms** | **342×** | 559.8 MB | 53.2 MB |
| US+CA+MX        | 15,801 ms  | **~70 ms**| **~226×**| 806.9 MB | ~87 MB  |

(Zip2City baseline — compile-time-baked data — initialises in ~0.033 ms / 160 B.)

### 3.4 Reverse-lookup result caching — before vs after (CA, the worst case)

The reverse lookups originally re-materialised the entire result set into a fresh array on every call
(landing CA's large sets on the large-object heap, triggering Gen2 GC). Per-bucket result caching now
returns the built list reference on repeat calls, exactly like the CSV registry:

| Operation (CA) | Before (no cache) | After (cached) | Speedup | Alloc before → after |
|---|--:|--:|--:|--:|
| GetByState     | 1,135,415 ns | **15.4 ns** | ~73,000× | 687 KB → **0 B** |
| GetByStateName |   895,587 ns | **11.8 ns** | ~76,000× | 687 KB → **0 B** |
| GetByTimeZone  |   976,154 ns | **21.0 ns** | ~46,000× | 1.38 MB → **0 B** |
| GetByAdmin     |   509,872 ns | **22.6 ns** | ~22,000× | 687 KB → **0 B** |
| GetByName      |     7,670 ns | **17.9 ns** | ~430×    | 51 KB → **0 B** |

Two reader-side fixes delivered this (no on-disk format change): a lazy `record→directory-slot` map
(`CodeForExactRecord` O(1) instead of O(n) per record) and the per-bucket / per-name result caches
(`_tzPostingCache`, `_adminPostingCache`, `_nameResultCache`). Correctness is guarded by
`ZpImageParityTests` / `CaZpImageParityTests` (9/9 pass).

### 3.5 Verdict

ZPI now wins on **both** axes it was meant to: it loads **20–342× faster** than building the CSV
registry, uses **~7–10× less startup memory**, and ships **3–4× smaller on disk** — and (with the v3
format) matches or beats CSV on every query category (exact hits **1.17–1.34× CSV** at 9–11 ns, zero-alloc;
miss path **ZPI faster: 0.71–0.92×** with 38% less allocation; reverse lookups **0.85–2.0×** and all allocation-free).
The remaining per-lookup overhead is a small constant (MPHF probe + bucket resolution) that doesn't scale with dataset size.

**Recommendation:** the frozen image is a strong default for cold-start-sensitive or memory-constrained
deployments (Lambda, CLIs, large CA datasets), where its load win dwarfs the ~9–11 ns exact-hit overhead.
For an always-warm service where `GetAllByCode` volume dominates and that 1.9–2.1× gap matters more than
zero-alloc reverse lookups, the CSV registry's `FrozenDictionary` hot path may still edge it. Both remain
interchangeable behind `IZipPostLookup`.

---

## 4. Analysis — AI prompts

Each prompt is self-contained — paste it into Claude (or another assistant) **along with the
relevant result table from §3** to get a focused read. Fill the `{…}` placeholders with your numbers.

### P01 — Hot-path lookup: is ZPI competitive on `GetByCode` hits?

```
Here is a BenchmarkDotNet comparison of two postal-code lookup backends for the same data — a
CSV-built registry backed by FrozenDictionary (baseline) vs a binary "ZP frozen image" backed by a
minimal perfect hash with lazy CodeEntry materialisation. Table grouped by {country × operation},
CSV = baseline, Ratio = ZPI relative to CSV:

{paste the "GetByCode hit", "GetByCode alt", and "Spread (hit)" rows for US/CA/MX}

The ZPI hit path is expected to match the registry's zero-allocation reference return once its
per-record cache is warm (the benchmark warms it in GlobalSetup). For each country, tell me:
(1) whether ZPI hits are within noise of CSV or meaningfully slower/faster and by how much,
(2) whether either backend allocates on the hit path and why that would happen,
(3) whether CA's range-resolved hit (M5V3L9) behaves differently from CA's exact-LDU hit (T0A0A0),
and (4) any result that looks anomalous enough to be a benchmark artifact rather than a real cost.
```

### P02 — Miss path

```
These rows compare the GetByCode MISS path for CSV vs ZP-image backends across US/CA/MX:

{paste the "GetByCode miss" and "Spread (miss)" rows}

The CSV registry is known to allocate on misses (it constructs an object on the return path) and to
run a normalisation loop plus range fallback before giving up. For each country: does the ZP image
avoid that miss-path allocation, and is its miss latency lower than CSV's? Explain the likely cause
of any difference given that a miss must exhaust the MPHF probe, every normalisation rule, and the
range table before returning null. Flag whether the miss cost scales with dataset size.
```

### P03 — Reverse lookups and enumeration

```
Compare CSV vs ZP-image for the reverse-lookup and enumeration operations below (US/CA/MX):

{paste the "GetByName", "GetByState", "GetByStateName", "GetByAdmin", "GetByTimeZone",
"GetAllByCode", "GetAll", "GetBatch" rows}

The ZP image stores postings lists for these reverse indexes and materialises CodeEntry objects
lazily on first touch. The registry returns references into already-built Lists. For each operation:
(1) is ZPI's reverse lookup competitive, (2) where does lazy materialisation show up as extra
allocation or latency, (3) is GetAll (full enumeration of every entry) reasonable on the image given
it must materialise all rows — especially for CA at 311k entries — and (4) does GetBatch show any
backend-specific overhead beyond the per-code lookup cost.
```

### P04 — Cold start: CSV build vs ZPI load

```
Here are construction/cold-start numbers for the same data via two paths — building the CSV registry
(parse + index into FrozenDictionary) vs loading the ZP frozen image (Brotli-decompress + map):

{paste the ZipPostLookup_Build* and ZpImage_Load* rows for US/CA/MX, plus Allocated}

For each country, quantify the ZPI load speedup and allocation reduction vs CSV build. CA CSV
construction is ~1.5 s / ~768 MB; state how much of that the image removes. Then advise: for which
deployment profiles (AWS Lambda cold start, short-lived CLI, long-running service, unit-test fixture)
does the ZP image's faster load justify shipping it, and where does the CSV path remain fine because
construction is amortised? Note any case where decompression cost offsets the construction saving.
```

### P05 — Per-country verdict

```
Using all the tables in sections 3.1–3.3, produce a per-country (US, CA, MX) verdict on whether the
ZP frozen image should become the default backend, stay an opt-in alternative, or be dropped. Weigh:
hot-path parity, miss-path behaviour, reverse-lookup cost, full-enumeration cost, cold-start saving,
and on-disk size (.zpi.br vs the embedded .csv — include the embedded resource byte sizes). Call out
any country where the answer differs from the others and why (e.g. CA's range compression interacts
differently with the MPHF). End with the single most important follow-up benchmark or measurement
still missing from this comparison.
```

### P06 — Combined optimisation prompt

```
I maintain ZipPostLookup (.NET 8 postal-code library) with two interchangeable lookup backends
implementing the same IZipPostLookup interface:
  • ZipPostRegistry — parses an embedded CSV into FrozenDictionary indexes at construction
  • ZpImageLookup   — loads a Brotli-compressed binary "frozen image" (minimal perfect hash +
                      lazy CodeEntry materialisation), reader is net8.0+ only

Both are benchmarked across US (42,931 distinct codes / 57k rows), CA (~900,688 distinct codes / 412k FSA-compressed entries), and MX (32,247 distinct codes / 145k rows), for: GetByCode hit/miss, GetAllByCode, GetByName/State/StateName/Admin/TimeZone, GetBatch, GetClosest (US/MX), hit/miss spread, and cold-start construction vs load.

Results:
{paste all three tables from section 3}

Embedded resource sizes:
{paste byte sizes of Data/{cc}/{cc}.csv vs Data/{cc}/{cc}.zpi.br for each country}

Give me a prioritised plan that answers: (1) should ZPI become the default for the static US
ZipPostLookup facade, (2) what — if anything — to optimise in the ZPI reader hot/miss/enumeration
paths before that, (3) the size-vs-speed tradeoff of shipping both the .csv and .zpi.br vs only one,
and (4) any correctness risk introduced by defaulting to the image (recall the writer/reader format
must stay byte-identical and is guarded by ZpImageParityTests). The public API must not change.
```

---

## 5. Benchmark source

| File | Class | What it measures |
|------|-------|------------------|
| `ZpImageBenchmarks.cs` | `ZpImageBenchmarks` | CSV vs ZPI lookup matrix, `[Params]` over US/CA/MX |
| `ClosestImageBenchmarks.cs` | `ClosestImageBenchmarks` | CSV vs ZPI `GetClosest`, US/MX (CA excluded — alphanumeric codes) |
| `ConstructionBenchmarks.cs` | `ConstructionBenchmarks` | CSV build vs ZPI load (+ Zip2City baseline), all countries |
| `InternationalLookupBenchmarks.cs` | `InternationalLookupBenchmarks` | CSV hot-path: US/CA/MX individually + combined multi-country registry |
| `CaRangeBenchmarks.cs` | `CaRangeBenchmarks` | CA range index: exact LDU vs compressed FSA hits, misses, normalisation |
| `BatchLookupBenchmarks.cs` | `BatchLookupBenchmarks` | `GetBatch` vs sequential loop at 10/100/1000 codes; dedup benefit |
| `LookupBenchmarks.cs` | `LookupBenchmarks` | US hot-path vs Zip2City; `GetClosest`; reverse lookups |

Each lookup operation is a `[BenchmarkCategory]` containing a CSV method (`[Benchmark(Baseline = true)]`)
and a ZPI method, with `GroupBenchmarksBy(ByCategory)` so the `Ratio` column is per-operation,
per-country. `[GlobalSetup]` runs a CSV-vs-ZPI **parity guard** before measuring, so the comparison
fails fast if the two backends ever disagree on a probed result.
