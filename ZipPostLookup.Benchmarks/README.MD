# ZipPostLookup.Benchmarks

BenchmarkDotNet harness for measuring the performance of `ZipPostLookup` against
[Zip2City](https://github.com/ShereSoft/Zip2City) (ShereSoft).

> **This project is not part of the NuGet package.**
> It is a developer tool for validating performance characteristics during development.
> It references `Zip2City` solely as a comparison baseline.

---

## Requirements

| Requirement   | Minimum                                                |
|---------------|--------------------------------------------------------|
| .NET SDK      | 8.0                                                    |
| Configuration | **Release** — Debug builds produce meaningless numbers |

---

## Running the benchmarks

From the solution root or the project directory:

```bash
# Run all benchmark classes
dotnet run -c Release --project ZipPostLookup.Benchmarks

# Run a specific class
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*LookupBenchmarks*"
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*ConstructionBenchmarks*"

# Run the full CSV-vs-ZPI comparison (recommended after any data export)
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*ZpImageBenchmarks*" "*ClosestImageBenchmarks*" "*ConstructionBenchmarks*"

# Run a specific method
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*GetByZip_Hit*"
```

Results are written to:

```
ZipPostLookup.Benchmarks/BenchmarkDotNet.Artifacts/results/
```

---

## Benchmark classes

### `LookupBenchmarks` — hot-path US vs Zip2City

Single-registry US benchmarks with both registries fully warmed up in `[GlobalSetup]`,
so construction cost does not pollute results.

| Benchmark | What it measures |
|---|---|
| `GetByCode` — hit | Single lookup for a code that exists |
| `GetByCode` — miss | Single lookup for a code that does not exist |
| `GetAllByCode` | All city entries for a multi-city code |
| `GetClosest` | Nearest code when no exact match exists |

---

### `ZpImageBenchmarks` — CSV vs ZP frozen image, all countries

Parameterised over US, CA, and MX. Every operation is a paired CSV (baseline) + ZPI method
within a `[BenchmarkCategory]`, so the `Ratio` column reads as *ZPI relative to CSV* per
operation per country. A parity guard in `[GlobalSetup]` fails the run if CSV and ZPI return
different results for any probe code.

Operations covered: `GetByCode` (hit, miss, alt), `GetAllByCode`, `GetBatch`, `GetByAdmin`,
`GetByName`, `GetByState`, `GetByStateName`, `GetByTimeZone`, `Spread` (hit + miss).

---

### `ClosestImageBenchmarks` — `GetClosest` CSV vs ZPI

`GetClosest` for US and MX only (CA codes are alphanumeric and `GetClosest` rejects them).
Also runs a parity guard in `[GlobalSetup]`.

```bash
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*ZpImageBenchmarks*" "*ClosestImageBenchmarks*" "*ConstructionBenchmarks*"
```

---

### `ConstructionBenchmarks` — cold-start cost

One-time cost of building each registry. This is the cost paid at application startup —
Lambda cold-start, unit test fixture setup, etc.

| Benchmark | What it measures |
|---|---|
| Build US/CA/MX CSV registries | Parse and index rows into `FrozenDictionary` indexes |
| Build US+CA+MX combined | All three into one combined registry |
| Load ZP image US/CA/MX | Brotli-decompress + map the `{cc}.zpi.br` frozen image |
| Zip2City type initialisation | Force Zip2City's static constructor (compile-time baked data) |

---

### `LegacyVsModernBenchmarks` — `Dictionary` vs `FrozenDictionary` hot path

Isolates the difference between the netstandard2.0 and net8.0 lookup engines by building
both a plain `Dictionary<string, CodeEntry>` and a `FrozenDictionary<string, CodeEntry>`
from the same live US registry data, then benchmarking hit and miss paths for each.
Zip2City is included as an external reference point.

---

### `MultiCountryBenchmarks` — combined US+CA+MX registry

A single `ZipPostRegistry` combining all three countries. Demonstrates that the combined
registry has no per-lookup dispatch overhead — one shared `FrozenDictionary`, one index
set. Also exercises CA range resolution and cross-country timezone queries.

| Benchmark | What it measures |
|---|---|
| `GetByCode` US zip | Baseline plain hash hit |
| `GetByCode` CA postal | FSA range resolution overhead |
| `GetByCode` MX zip | MX normaliser overhead |
| `GetByTimeZone` (US+CA) | Cross-country timezone query |

---

### `BatchLookupBenchmarks` — `GetBatch` vs sequential loop

Compares `GetBatch(CodeBatch)` against an equivalent `for` loop of `GetByCode` calls
at sizes 10, 100, and 1,000 codes.

---

## `GetBatch` usage

`GetBatch` looks up multiple postal codes in one call and returns an
`IReadOnlyDictionary<string, CodeEntry?>`.

```csharp
// Pre-build the batch once — reuse it across requests
var batch = new CodeBatch()
    .Add("10001")
    .Add("90210")
    .Add("10001")   // silently deduped
    .AddRange(codesFromCsv);

var results = ZipPostLookup.GetBatch(batch);

foreach (var (code, entry) in results)
    Console.WriteLine(entry != null ? $"{code} → {entry.PlaceName}" : $"{code} → not found");

// Convenience overload — array of strings, duplicates silently ignored
var results2 = ZipPostLookup.GetBatch(new[] { "10001", "90210", "10001" });
```

> **Performance note:** `GetBatch` is **slower than a sequential loop** for unique inputs
> (~1.4× at any tested size) due to `Dictionary` return-value construction overhead.
> Its advantage is deduplication — if your input contains duplicate codes, each duplicate
> is resolved once rather than repeatedly. Build the `CodeBatch` once and reuse across calls.

| Scenario | Recommendation |
|---|---|
| Single code or small unique list | `GetByCode` directly |
| Large list of unique codes | Sequential loop with `GetByCode` |
| List with expected duplicates | `GetBatch` with pre-built `CodeBatch` |

---

## Interpreting results

BenchmarkDotNet reports `Mean`, `Error`, `StdDev`, `Ratio`, `Gen0`, and `Allocated` for
each method. The most important columns for this library are:

- **Mean** — average execution time in nanoseconds (ns) or milliseconds (ms)
- **Allocated** — managed heap bytes allocated per operation; `—` means zero allocations
- **Ratio** — relative to the baseline method within each group

Zero allocation on `GetByCode` hits is a deliberate design goal. The registry returns a
reference to an already-live `CodeEntry` stored in the `FrozenDictionary` — no new objects
are created on the hot path.

---

## Latest results

```
Windows 11 (10.0.22631.6199 / 23H2)
13th Gen Intel Core i7-13700K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.301 — Runtime: .NET 8.0.28, X64 RyuJIT x86-64-v3
Benchmarked: 2026-06-12
```

> **What changed since 2026-06-08:** a quadratic insert in CSV registry construction (`AddToIndex`'s
> `Insert(0)` on large posting lists) was fixed. The CA build dropped from **~33 s to ~1.4 s** (24×),
> with US/MX builds also faster. Lookup hot paths are unchanged (within noise). See
> `History/CA-CONSTRUCTION-INVESTIGATION.md` for the full investigation.

### Hot-path lookups — US, vs Zip2City

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZipPostLookup — GetByCode (hit) | 5.83 ns | 1.00 | 0 B |
| ZipPostLookup — GetAllByCode | 8.98 ns | 1.54 | 24 B |
| ZipPostLookup — GetByCode (miss) | 31.31 ns | 5.37 | 104 B |
| ZipPostLookup — GetClosest (no match) | 35.71 ns | 6.13 | 32 B |
| Zip2City — GetDefaultCityState (hit) | 32.76 ns | 5.62 | 160 B |
| Zip2City — GetDefaultCityState (miss) | 32.80 ns | 5.63 | 160 B |
| Zip2City — GetAllCityStates | 43.00 ns | 7.38 | 160 B |
| Zip2City — GetClosest (no match) | 1,064.61 ns | 182.62 | 160 B |

### `Dictionary` vs `FrozenDictionary` (legacy vs modern hot path)

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Dictionary — hit | 8.56 ns | 1.00 | 0 B |
| FrozenDictionary — hit | 7.42 ns | 0.87 | 0 B |
| Zip2City — hit | 31.58 ns | 3.69 | 160 B |
| Dictionary — miss | 5.77 ns | 0.67 | 0 B |
| FrozenDictionary — miss | 3.18 ns | 0.37 | 0 B |
| Zip2City — miss | 29.75 ns | 3.48 | 160 B |

FrozenDictionary is 13% faster on hits and 45% faster on misses, both at zero allocation.

### Construction cost — CSV registry build vs ZP image load

| Method | Mean | Allocated |
|---|---:|---:|
| Build US registry (60k rows, curated) | 136 ms | 81 MB |
| Build MX registry (146k rows) | 375 ms | 189 MB |
| Build CA registry (603k rows, FSA-compressed) | **1,373 ms** | **817 MB** |
| Build US+CA+MX combined | 2,186 ms | 1,073 MB |
| **Load US ZP image (`us.u16.zpi.br`)** | **7.8 ms** | **2.8 MB** |
| **Load MX ZP image (`mx.u32.zpi.br`)** | **14.3 ms** | **6.3 MB** |
| **Load CA ZP image (`ca.u16.zpi.br`)** | **55.2 ms** | **26 MB** |
| Zip2City — type initialisation | 0.032 ms | 160 B |

Construction cost is paid once at startup. The **ZP frozen image** loads the same data
**17–26× faster** with **~29–31× less memory** — the strongest reason to ship the image for
cold-start-sensitive deployments (Lambda, containers, unit test fixtures). The time gap used to be
hundreds of × (CA built in ~33 s); the construction fix narrowed it, but the memory gap widened.

### CSV vs ZP image — single-lookup comparison (US, representative)

| Operation | CSV | ZPI | Winner |
|---|---:|---:|---|
| GetByCode hit | 6.16 ns, 0 B | 8.13 ns, 0 B | CSV (1.32×) |
| GetByCode miss | 30.46 ns, 104 B | 24.20 ns, 64 B | **ZPI (0.79×, 38% less mem)** |
| GetAllByCode | 8.96 ns, 24 B | 14.12 ns, 40 B | CSV (1.58×) |
| GetByAdmin | 17.73 ns, 80 B | 16.93 ns, **0 B** | **ZPI (0.95×, zero-alloc)** |
| GetByName | 11.29 ns, 24 B | 15.52 ns, **0 B** | CSV speed, **ZPI zero-alloc** |
| GetByState | 6.05 ns, 24 B | 7.93 ns, **0 B** | CSV speed, **ZPI zero-alloc** |
| GetByTimeZone | 8.41 ns, 24 B | 10.59 ns, **0 B** | CSV speed, **ZPI zero-alloc** |
| GetClosest | 44.16 ns, 32 B | 44.10 ns, 32 B | **Dead heat** |

ZPI eliminates allocations entirely on all secondary-index lookups (admin, name, state,
timezone). CSV is faster on individual hits by ~1.2–2 ns.

### Multi-country combined registry overhead (US+CA+MX)

| Code type | Mean | vs US-only |
|---|---:|---:|
| US zip | 6.06 ns | baseline |
| MX zip | 6.41 ns | 1.06× |
| CA postal (FSA range resolve) | 6.61 ns | 1.09× |
| GetByTimeZone spanning US + CA | 9.62 ns | 1.59× |

The combined registry adds essentially no per-country dispatch overhead — CA range resolution and
the MX normaliser land within ~10% of the plain US hash hit.

### `GetBatch` vs sequential

| Size | Sequential | CodeBatch | Batch overhead |
|---|---:|---:|---:|
| 10 codes | 302.6 ns | 413.6 ns | 1.37× |
| 100 codes | 3,139.3 ns | 4,218.7 ns | 1.34× |
| 1,000 codes | 30,879.1 ns | 42,248.4 ns | 1.37× |

---

## Known performance characteristics

### `GetByCode` miss path

Miss lookups allocate 104 B and take ~31 ns — significantly slower than hits (5.8 ns, 0 B).
The miss path runs the full normaliser chain (normalise → validate → range fallback) before
returning null. Hits short-circuit at the first `FrozenDictionary` probe.

### CA construction time (CSV)

The Canadian dataset is the heaviest CSV registry build at **~1.4 s** (603k compressed entries
covering ~900k distinct codes, ~817 MB). This was **~33 s** until the 2026-06-12 fix to a
quadratic insert in `AddToIndex` (`Insert(0)` shifting large posting lists on every default
entry — see `History/CA-CONSTRUCTION-INVESTIGATION.md`). It is still the strongest case for
shipping the ZP frozen image: `ca.u16.zpi.br` loads the same data in **~55 ms** (~25× faster,
~26 MB, ~30× less allocation). For the CSV path, pre-warming the registry in a background task
at startup remains a sensible mitigation for cold-start-sensitive deployments.

### `GetBatch` for unique inputs

`GetBatch` is consistently slower than a sequential loop (~1.4×) when all input codes are
unique. The overhead is the `Dictionary<string, CodeEntry?>` result allocation. `GetBatch`
only pays off when the input contains meaningful duplicates.

---

## Adding a new benchmark

Add a method to an existing class or create a new class in the project:

- The class must be `public`
- The method must be `public` and decorated with `[Benchmark]`
- Run with `dotnet run -c Release` — not from the IDE run button (uses Debug)

```csharp
[Benchmark(Description = "ZipPostLookup — GetByStateName")]
public IReadOnlyList<CodeEntry> GetByStateName() =>
    _registry.GetByStateName("California");
```

BenchmarkDotNet discovers all `[Benchmark]`-decorated public methods in the assembly
automatically — no registration needed.
