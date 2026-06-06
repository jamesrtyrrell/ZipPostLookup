# ZipPostLookup vs Zip2City — Benchmark Results

Benchmarks run with [BenchmarkDotNet](https://benchmarkdotnet.org/) v0.15.8 comparing
**ZipPostLookup** against **[Zip2City](https://github.com/ShereSoft/Zip2City)** (ShereSoft).

**Environment**

```
Windows 11 (10.0.22631.6199 / 23H2)
13th Gen Intel Core i7-13700K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.300 — Runtime: .NET 8.0.27, X64 RyuJIT AVX2
```

> Benchmarks were run with `dotnet run -c Release --project ZipPostLookup.Benchmarks`.
> Results are stored under `ZipPostLookupBenchmarks/BenchmarkDotNet.Artifacts/results/`.
> All numbers are the mean across **4 independent runs** on the same machine to eliminate thermal noise.

---

## At a glance — head to head

**Legend:** ✅ wins / supported · ❌ loses / unsupported · ≈ effectively tied

| Metric / Capability | ZipPostLookup | Zip2City |
|---|---|---|
| **Hit lookup** (`GetByZip`/`GetByCode`) | ✅ **6.2 ns** · **0 B** | ❌ 35.8 ns · 160 B |
| **Miss lookup** | ✅ **34.6 ns** · **104 B** | ❌ 36.8 ns · 160 B |
| **All names for a code** (`GetAllByCode`) | ✅ **9.5 ns** · 24 B | ❌ 48.3 ns · 160 B |
| **Closest code** (no exact match) | ✅ **38.6 ns** · 32 B | ❌ 1,132 ns · 160 B |
| **10× lookup spread** | ✅ **69.0 ns** · **0 B** | ❌ 386.3 ns · 1,600 B |
| **5× miss spread** | ✅ **120 ns** · **312 B** | ❌ 190 ns · 800 B |
| **Zero-allocation hot path** | ✅ Yes | ❌ No — 160 B every call |
| **Multi-country** | ✅ US · CA · MX (+ extensible) | ❌ US only |
| **Reverse lookups** (name / state / admin / timezone) | ✅ Yes | ❌ None |
| **Typed result** | ✅ `ZipEntry` + `AdminLevel[]` | ❌ `string[]` |
| **Cold-start construction** | ❌ ~189 ms (runtime CSV parse) ¹ | ✅ ~0.04 ms (compile-time baked) |

¹ One-time startup cost, then amortised to zero. The optional **ZP frozen image** cuts the US
load to ~10 ms (and CA from ~18 s to ~41 ms) — see [ZpiVsCsvBenchmark.MD](ZpiVsCsvBenchmark.MD).

**Net:** ZipPostLookup wins every lookup operation — decisively on hits (5.7× faster, zero-alloc),
all-names (5.1×), closest (~29×), and spreads (~6×). The **miss** path also favours ZipPostLookup
(34.6 ns vs 36.8 ns, 38% leaner allocation). Zip2City's only clear win is **construction**, because
it bakes its data as a compile-time literal; ZipPostLookup pays a one-time parse at startup (or ships
the frozen image to erase most of it).

> **API note:** `GetByCode` and `GetByZip` are overloads — identical implementations,
> different names for readability. The same pairing applies to `TryGetByCode`/`TryGetByZip`,
> `GetAllByCode`/`GetAllByZip`, and `GetByName`/`GetByCity`. Benchmark results apply equally
> to both names in each pair.

> **Note on dataset size:** The US dataset contains 42,931 distinct ZIP codes (57k rows including
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
| ZipPostLookup — GetByCode / GetByZip (hit) | **6.249 ns** | 1.00 | **0 B** |
| Zip2City — GetDefaultCityState (hit) | 35.842 ns | 5.73 | 160 B |
| ZipPostLookup — GetByCode / GetByZip (miss) | **34.620 ns** | 5.54 | 104 B |
| Zip2City — GetDefaultCityState (miss) | 36.789 ns | 5.89 | 160 B |
| ZipPostLookup — GetAllByCode / GetAllByZip (multi-city) | **9.476 ns** | 1.52 | 24 B |
| Zip2City — GetAllCityStates | 48.270 ns | 7.72 | 160 B |
| ZipPostLookup — GetClosest (no exact match) | **38.561 ns** | 6.17 | 32 B |
| Zip2City — GetClosestCityState (no exact match) | 1,131.927 ns | 181.14 | 160 B |
| ZipPostLookup — 10× GetByCode / GetByZip spread | **68.995 ns** | 11.04 | **0 B** |
| Zip2City — 10× GetDefaultCityState spread | 386.326 ns | 61.82 | 1,600 B |
| ZipPostLookup — 5× GetByCode miss spread | 120.405 ns | 19.27 | 312 B |
| Zip2City — 5× GetDefaultCityState miss spread | 190.481 ns | 30.48 | 800 B |

### Why ZipPostLookup is faster on hits

Zip2City allocates a new `string[]` on every call — hit or miss — resulting in 160 B of heap
pressure per lookup. ZipPostLookup returns a direct reference to an already-live `ZipEntry`
stored in the `FrozenDictionary`, so hit lookups allocate nothing. On .NET 8+,
`FrozenDictionary` uses a perfect-hash scheme that eliminates locking overhead and improves
CPU cache utilisation over a standard `Dictionary`.

### Miss path note

ZipPostLookup now wins on both latency (34.6 ns vs 36.8 ns) and allocation (104 B vs 160 B) on
the miss path. The 104 B comes from the normalisation rule pipeline — each `ICountryCodeRules`
implementation calls `.Normalize()` which may allocate intermediate strings. Short-circuiting
normalisation for clearly invalid inputs remains a known optimisation opportunity.

---

## ZipPostLookup only

These methods have no Zip2City equivalent and are shown for reference.

| Method | Mean | Allocated |
|---|---:|---:|
| GetClosest (no exact match) | 38.6 ns | 32 B |
| GetByName / GetByCity | 9.4 ns | 24 B |
| GetByState | 7.5 ns | 24 B |
| GetByTimeZone | 9.3 ns | 24 B |

### `GetClosest` — O(log n) binary search

`GetClosest` uses a sorted `int[]` of all US zip codes built at construction time with
`Array.BinarySearch` at query time — O(log n). At ~38.6 ns it is **~29× faster** than
Zip2City's equivalent at ~1,132 ns.

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
| ZipPostLookup — build US registry (57k rows, curated) | 188.8 ms | 81.8 MB |
| ZipPostLookup — build MX registry (145k rows) | 653.9 ms | 191.7 MB |
| ZipPostLookup — build CA registry (412k rows, FSA-compressed) | 17,844 ms | 590.0 MB |
| ZipPostLookup — build US+CA combined (North America) | 18,030 ms | 665.6 MB |
| ZipPostLookup — build US+CA+MX combined (North America full) | 18,882 ms | 844.9 MB |
| ZP image — load US frozen image (Brotli) | 9.7 ms | 4.0 MB |
| ZP image — load CA frozen image (Brotli) | 41.3 ms | 20.4 MB |
| ZP image — load MX frozen image (Brotli) | 22.1 ms | 11.2 MB |
| Zip2City — force type initialisation | 0.04 ms | 160 B |

> Measured by `ConstructionBenchmarks` (`--filter "*ConstructionBenchmarks*"`). Zip2City bakes its
> data as a compile-time literal, so its "construction" is just static-ctor init — essentially free.

The construction cost is paid once at application startup. For the US-only registry (the common case)
it is ~189 ms. Canada dominates at ~17.8 s because of its 412k-row dataset (now fully populated with
province data) — a strong reason to ship the **ZP frozen image** for CA, which loads in ~41 ms
(a ~432× cold-start win). See [ZpiVsCsvBenchmark.MD](ZpiVsCsvBenchmark.MD) for the full
CSV-build-vs-image-load comparison.

---

## Reproducing these results

```bash
# All benchmarks
dotnet run -c Release --project ZipPostLookup.Benchmarks

# Single class
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*LookupBenchmarks*"
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*ConstructionBenchmarks*"
```

Results are written to `ZipPostLookupBenchmarks/BenchmarkDotNet.Artifacts/results/`.

---

## Batch lookups — `GetBatch`

`GetBatch` accepts a `ZipCodeBatch` (deduplicated collection) or `IEnumerable<string>` and
returns a `IReadOnlyDictionary<string, ZipEntry?>` mapping each input code to its result.

```
Job=.NET 8.0  Runtime=.NET 8.0
```

| Method | Mean | Gen0 | Gen1 | Allocated |
|---|---:|---:|---:|---:|
| Sequential — 10 `GetByCode` calls | 363.6 ns | 0.0663 | — | 1.02 KB |
| Batch — 10 codes (`ZipCodeBatch`) | 527.5 ns | 0.0963 | — | 1.48 KB |
| Batch — 10 codes (IEnumerable overload) | 668.2 ns | 0.1497 | — | 2.30 KB |
| Sequential — 100 `GetByCode` calls | 3,502.5 ns | 0.5035 | — | 7.72 KB |
| Batch — 100 codes (`ZipCodeBatch`) | 4,736.6 ns | 0.7019 | 0.0076 | 10.81 KB |
| Sequential — 1000 `GetByCode` calls | 33,779.9 ns | 3.8452 | — | 59.21 KB |
| Batch — 1000 codes (`ZipCodeBatch`) | 47,057.5 ns | 5.7983 | 0.5493 | 89.54 KB |
| Sequential — 200 codes, 50% duplicates | 7,248.8 ns | 1.0071 | — | 15.44 KB |
| **Batch — 200 codes, 50% duplicates (100 unique)** | **4,601.8 ns** | 0.7019 | 0.0076 | 10.81 KB |
| `ZipCodeBatch` construction — 100 codes (single-threaded) | 1,194.0 ns | 0.4749 | 0.0076 | 7.27 KB |
| `ZipCodeBatch` construction — 100 codes (concurrent) | 3,885.2 ns | 0.8087 | 0.0305 | 12.39 KB |

### Key findings

**Batch is slower than sequential for unique inputs.** `GetBatch` must allocate and populate
a `Dictionary<string, ZipEntry?>` as its return value — this overhead outweighs any per-lookup saving
for clean unique input sets (~1.35× at 100 and 1000 codes, ~1.45× at 10 codes). There is no crossover
point for pre-deduplicated inputs.

**Batch wins when duplicates are present.** 200 codes with 50% duplicates:
sequential does 200 lookups in 7,249 ns; batch deduplicates to 100 unique lookups and
finishes in 4,602 ns — a **~37% saving**. The more duplicates, the larger the gain.

**`ZipCodeBatch` construction is not free.** At ~1,194 ns for 100 codes, construction
costs roughly 3× a sequential 10-code pass (364 ns). Build the batch once and
reuse it across requests rather than constructing per-call.

**Use the `IEnumerable` overload sparingly.** It creates a `ZipCodeBatch` internally on
every call (2.30 KB vs 1.48 KB at 10 codes). When you control the input, construct a
`ZipCodeBatch` once and pass that directly.

**Concurrent construction is 3× slower.** Use `concurrent: true` only when multiple threads
genuinely add codes simultaneously — for single-threaded builds the default is significantly faster.

### When to use `GetBatch`

| Scenario | Recommendation |
|---|---|
| Validating a form field or single API call | `GetByCode` directly — no batch needed |
| Processing a unique list of 10–1000 codes | Sequential loop — batch adds overhead |
| Processing a list with expected duplicates | `GetBatch` with pre-built `ZipCodeBatch` |
| Bulk import / ETL with many repeated codes | `GetBatch` — deduplication pays off at scale |
| Multi-threaded code building the input list | `new ZipCodeBatch(concurrent: true)` |