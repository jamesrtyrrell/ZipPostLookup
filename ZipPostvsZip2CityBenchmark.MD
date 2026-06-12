# ZipPostLookup vs Zip2City — Benchmark Results

Benchmarks run with [BenchmarkDotNet](https://benchmarkdotnet.org/) v0.15.8 comparing
**ZipPostLookup** against **[Zip2City](https://github.com/ShereSoft/Zip2City)** (ShereSoft).

**Environment** — benchmarked **2026-06-12**

```
Windows 11 (10.0.22631.6199 / 23H2)
13th Gen Intel Core i7-13700K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.301 — Runtime: .NET 8.0.28, X64 RyuJIT x86-64-v3
```

> Benchmarks were run with `dotnet run -c Release --project ZipPostLookup.Benchmarks`.
> Results are stored under `ZipPostLookup.Benchmarks/BenchmarkDotNet.Artifacts/results/`.
> Lookup classes use BenchmarkDotNet's adaptive engine; `ConstructionBenchmarks` is pinned to
> 6 iterations. Dataset: US 59,719 rows (43,498 distinct codes), CA 603,272 FSA-compressed
> entries (~900,686 distinct codes), MX 146,183 rows (32,893 distinct codes).

---

## At a glance — head to head

**Legend:** ✅ wins / supported · ❌ loses / unsupported · ≈ effectively tied

| Metric / Capability | ZipPostLookup | Zip2City |
|---|---|---|
| **Hit lookup** (`GetByZip`/`GetByCode`) | ✅ **5.8 ns** · **0 B** | ❌ 32.8 ns · 160 B |
| **Miss lookup** | ✅ **31.3 ns** · **104 B** | ❌ 32.8 ns · 160 B |
| **All names for a code** (`GetAllByCode`) | ✅ **9.0 ns** · 24 B | ❌ 43.0 ns · 160 B |
| **Closest code** (no exact match) | ✅ **35.7 ns** · 32 B | ❌ 1,065 ns · 160 B |
| **Zero-allocation hot path** | ✅ Yes | ❌ No — 160 B every call |
| **Multi-country** | ✅ US · CA · MX (+ extensible) | ❌ US only |
| **Reverse lookups** (name / state / admin / timezone) | ✅ Yes | ❌ None |
| **Typed result** | ✅ `ZipEntry` + `AdminLevel[]` | ❌ `string[]` |
| **Cold-start construction** | ❌ ~136 ms (runtime CSV parse) ¹ | ✅ ~0.03 ms (compile-time baked) |

¹ One-time startup cost, then amortised to zero. The optional **ZP frozen image** cuts the US
load to ~7.8 ms (and CA to ~55 ms) — see [ZpiVsCsvBenchmark.MD](ZpiVsCsvBenchmark.MD).

**Net:** ZipPostLookup wins every lookup operation — decisively on hits (5.6× faster, zero-alloc),
all-names (4.8×), and closest (~30×). The **miss** path also favours ZipPostLookup
(31.3 ns vs 32.8 ns, 35% leaner allocation). Zip2City's only clear win is **construction**, because
it bakes its data as a compile-time literal; ZipPostLookup pays a one-time parse at startup (or ships
the frozen image to erase most of it).

> **API note:** `GetByCode` and `GetByZip` are overloads — identical implementations,
> different names for readability. The same pairing applies to `TryGetByCode`/`TryGetByZip`,
> `GetAllByCode`/`GetAllByZip`, and `GetByName`/`GetByCity`. Benchmark results apply equally
> to both names in each pair.

> **Note on dataset size:** The US dataset contains 43,498 distinct ZIP codes (59,719 rows including
> multi-name entries). Hit lookups return a direct reference with zero allocations.

---

## Head-to-head lookups

Both registries fully warmed up before measurement.
`GetByCode / GetByZip (hit)` is the baseline (Ratio = 1.00).

```
Job=.NET 8.0  Runtime=.NET 8.0
```

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZipPostLookup — GetByCode / GetByZip (hit) | **5.830 ns** | 1.00 | **0 B** |
| Zip2City — GetDefaultCityState (hit) | 32.762 ns | 5.62 | 160 B |
| ZipPostLookup — GetByCode / GetByZip (miss) | **31.311 ns** | 5.37 | 104 B |
| Zip2City — GetDefaultCityState (miss) | 32.803 ns | 5.63 | 160 B |
| ZipPostLookup — GetAllByCode / GetAllByZip (multi-city) | **8.975 ns** | 1.54 | 24 B |
| Zip2City — GetAllCityStates | 42.995 ns | 7.38 | 160 B |
| ZipPostLookup — GetClosest (no exact match) | **35.708 ns** | 6.13 | 32 B |
| Zip2City — GetClosestCityState (no exact match) | 1,064.614 ns | 182.62 | 160 B |

### Why ZipPostLookup is faster on hits

Zip2City allocates a new `string[]` on every call — hit or miss — resulting in 160 B of heap
pressure per lookup. ZipPostLookup returns a direct reference to an already-live `ZipEntry`
stored in the `FrozenDictionary`, so hit lookups allocate nothing. On .NET 8+,
`FrozenDictionary` uses a perfect-hash scheme that eliminates locking overhead and improves
CPU cache utilisation over a standard `Dictionary`.

### Miss path note

ZipPostLookup wins on both latency (31.3 ns vs 32.8 ns) and allocation (104 B vs 160 B) on
the miss path. The 104 B comes from the normalisation rule pipeline — each `ICountryCodeRules`
implementation calls `.Normalize()` which may allocate intermediate strings. Short-circuiting
normalisation for clearly invalid inputs remains a known optimisation opportunity.

---

## ZipPostLookup only

These methods have no Zip2City equivalent and are shown for reference.

| Method | Mean | Allocated |
|---|---:|---:|
| GetClosest (no exact match) | 35.7 ns | 32 B |
| GetByName / GetByCity | 11.3 ns | 24 B |
| GetByState | 6.1 ns | 24 B |
| GetByTimeZone | 8.4 ns | 24 B |

> Reverse-lookup numbers are the US CSV-registry figures from `ZpImageBenchmarks` (2026-06-12).

### `GetClosest` — O(log n) binary search

`GetClosest` uses a sorted `int[]` of all US zip codes built at construction time with
`Array.BinarySearch` at query time — O(log n). At ~35.7 ns it is **~30× faster** than
Zip2City's equivalent at ~1,065 ns.

The 32 B allocation is from `ToString("D5")` converting the nearest integer back to a
zero-padded zip string before the final dictionary lookup.

### Reverse lookups

`GetByName`, `GetByState`, and `GetByTimeZone` run at ~7 ns. The 24 B allocation is from
`.AsReadOnly()` wrapping the internal list for each call — a known optimisation opportunity
(caching the `ReadOnlyCollection` wrapper at index build time would bring this to 0 B).

---

## Construction cost

Zip2City bakes its data as a hardcoded dictionary literal at compile time — essentially free.
ZipPostLookup parses embedded CSVs at runtime and builds six `FrozenDictionary` indexes plus
a sorted zip array for `GetClosest` and a range index for Canadian FSA compression.

| Method | Mean | Allocated |
|---|---:|---:|
| ZipPostLookup — build US registry (60k rows, curated) | 136.0 ms | 81.1 MB |
| ZipPostLookup — build MX registry (146k rows) | 375.0 ms | 189.3 MB |
| ZipPostLookup — build CA registry (603k rows, FSA-compressed) | 1,373 ms | 816.6 MB |
| ZipPostLookup — build US+CA combined (North America) | 1,639 ms | 891.3 MB |
| ZipPostLookup — build US+CA+MX combined (North America full) | 2,186 ms | 1,073 MB |
| ZP image — load US frozen image (Brotli) | 7.8 ms | 2.8 MB |
| ZP image — load CA frozen image (Brotli) | 55.2 ms | 26.3 MB |
| ZP image — load MX frozen image (Brotli) | 14.3 ms | 6.3 MB |
| Zip2City — force type initialisation | 0.03 ms | 160 B |

> Measured by `ConstructionBenchmarks` (`--filter "*ConstructionBenchmarks*"`). Zip2City bakes its
> data as a compile-time literal, so its "construction" is just static-ctor init — essentially free.

The construction cost is paid once at application startup. For the US-only registry (the common case)
it is ~136 ms. Canada is the heaviest at ~1.37 s for its 603k-row dataset — **down from ~33 s** before
the 2026-06-12 fix to a quadratic insert in registry construction (`AddToIndex`). The **ZP frozen
image** still loads CA in ~55 ms (a ~25× cold-start win) and uses ~30× less startup memory, so it
remains the recommended option for cold-start-sensitive or memory-constrained deployments. See
[ZpiVsCsvBenchmark.MD](ZpiVsCsvBenchmark.MD) for the full CSV-build-vs-image-load comparison.

---

## Reproducing these results

```bash
# All benchmarks
dotnet run -c Release --project ZipPostLookup.Benchmarks

# Single class
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*LookupBenchmarks*"
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*ConstructionBenchmarks*"
```

Results are written to `ZipPostLookup.Benchmarks/BenchmarkDotNet.Artifacts/results/`.

---

## Batch lookups — `GetBatch`

`GetBatch` accepts a `CodeBatch` (deduplicated collection) or `IEnumerable<string>` and
returns a `IReadOnlyDictionary<string, CodeEntry?>` mapping each input code to its result.
Measured by `BatchLookupBenchmarks` (2026-06-12), baseline = the 10-code sequential loop.

```
Job=.NET 8.0  Runtime=.NET 8.0
```

| Method | Mean | Ratio | Gen0 | Allocated |
|---|---:|---:|---:|---:|
| Sequential — 10 `GetByCode` calls | 302.6 ns | 1.00 | 0.0596 | 936 B |
| Batch — 10 codes (`CodeBatch`) | 413.6 ns | 1.37 | 0.0901 | 1,416 B |
| Sequential — 100 `GetByCode` calls | 3,139.3 ns | 10.37 | 0.5035 | 7,904 B |
| Batch — 100 codes (`CodeBatch`) | 4,218.7 ns | 13.94 | 0.7019 | 11,072 B |
| Sequential — 1000 `GetByCode` calls | 30,879.1 ns | 102.04 | 3.7842 | 60,008 B |
| Batch — 1000 codes (`CodeBatch`) | 42,248.4 ns | 139.61 | 5.7983 | 91,064 B |

### Key findings

**Batch is slower than sequential for unique inputs.** `GetBatch` must allocate and populate
a `Dictionary<string, CodeEntry?>` as its return value — this overhead outweighs any per-lookup
saving for clean unique input sets (~1.34–1.37× at every tested size). There is no crossover
point for pre-deduplicated inputs.

**Batch wins when duplicates are present.** Its advantage is deduplication: each distinct code
is resolved once regardless of how many times it appears in the input, whereas the sequential
loop re-resolves every occurrence. The more duplicates, the larger the gain.

**Build the `CodeBatch` once and reuse it.** Batch construction is not free, so construct it
once and reuse it across requests rather than rebuilding per call. When you control the input,
pass a `CodeBatch` directly rather than relying on the `IEnumerable` overload (which builds one
internally on every call).

### When to use `GetBatch`

| Scenario | Recommendation |
|---|---|
| Validating a form field or single API call | `GetByCode` directly — no batch needed |
| Processing a unique list of 10–1000 codes | Sequential loop — batch adds overhead |
| Processing a list with expected duplicates | `GetBatch` with pre-built `CodeBatch` |
| Bulk import / ETL with many repeated codes | `GetBatch` — deduplication pays off at scale |