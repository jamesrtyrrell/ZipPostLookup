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
.NET SDK 10.0.300 — Runtime: .NET 8.0.27, X64 RyuJIT AVX2
Benchmarked: 2026-06-08
```

### Hot-path lookups — US, vs Zip2City

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZipPostLookup — GetByCode (hit) | 6.09 ns | 1.00 | 0 B |
| ZipPostLookup — GetAllByCode | 9.40 ns | 1.54 | 24 B |
| ZipPostLookup — GetByCode (miss) | 32.42 ns | 5.32 | 104 B |
| ZipPostLookup — GetClosest (no match) | 44.29 ns | 7.27 | 32 B |
| Zip2City — GetDefaultCityState (hit) | 33.88 ns | 5.56 | 160 B |
| Zip2City — GetDefaultCityState (miss) | 31.11 ns | 5.11 | 160 B |
| Zip2City — GetAllCityStates | 44.76 ns | 7.35 | 160 B |
| Zip2City — GetClosest (no match) | 1,051.98 ns | 172.71 | 160 B |

### `Dictionary` vs `FrozenDictionary` (legacy vs modern hot path)

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Dictionary — hit | 8.49 ns | 1.00 | 0 B |
| FrozenDictionary — hit | 6.79 ns | 0.80 | 0 B |
| Zip2City — hit | 37.98 ns | 4.48 | 160 B |
| Dictionary — miss | 6.12 ns | 0.72 | 0 B |
| FrozenDictionary — miss | 3.32 ns | 0.39 | 0 B |
| Zip2City — miss | 30.43 ns | 3.59 | 160 B |

FrozenDictionary is 20% faster on hits and 46% faster on misses, both at zero allocation.

### Construction cost — CSV registry build vs ZP image load

| Method | Mean | Allocated |
|---|---:|---:|
| Build US registry (57k rows, curated) | 184 ms | 85 MB |
| Build MX registry (145k rows) | 1,077 ms | 188 MB |
| Build CA registry (412k rows, FSA-compressed) | **27,466 ms** | **563 MB** |
| Build US+CA+MX combined | 17,361 ms | 819 MB |
| **Load US ZP image (`us.zpi.br`)** | **10 ms** | **4 MB** |
| **Load MX ZP image (`mx.zpi.br`)** | **25 ms** | **11 MB** |
| **Load CA ZP image (`ca.zpi.br`)** | **40 ms** | **20 MB** |
| Zip2City — type initialisation | 0.033 ms | 160 B |

Construction cost is paid once at startup. The **ZP frozen image** loads the same data
**18–690× faster** with **14–28× less memory** — the single strongest reason to ship the
image for cold-start-sensitive deployments (Lambda, containers, unit test fixtures).

### CSV vs ZP image — single-lookup comparison (US, representative)

| Operation | CSV | ZPI | Winner |
|---|---:|---:|---|
| GetByCode hit | 6.05 ns, 0 B | 8.24 ns, 0 B | CSV (1.36×) |
| GetByCode miss | 31.70 ns, 104 B | 24.61 ns, 64 B | **ZPI (0.78×, 38% less mem)** |
| GetAllByCode | 14.96 ns, 24 B | 27.62 ns, 40 B | CSV (1.85×) |
| GetByAdmin | 17.98 ns, 80 B | 17.57 ns, **0 B** | Tied, **ZPI zero-alloc** |
| GetByName | 11.04 ns, 24 B | 15.89 ns, **0 B** | CSV speed, **ZPI zero-alloc** |
| GetByState | 6.45 ns, 24 B | 8.07 ns, **0 B** | CSV speed, **ZPI zero-alloc** |
| GetByTimeZone | 8.45 ns, 24 B | 11.32 ns, **0 B** | CSV speed, **ZPI zero-alloc** |
| GetClosest | 35.90 ns, 32 B | 36.25 ns, 32 B | **Dead heat** |

ZPI eliminates allocations entirely on all secondary-index lookups (admin, name, state,
timezone). CSV is faster on individual hits by ~1.2–1.8 ns.

### Multi-country combined registry overhead (US+CA+MX)

| Code type | Mean | vs US-only |
|---|---:|---:|
| US zip | 5.53 ns | baseline |
| MX zip | 12.04 ns | 2.2× |
| CA postal (FSA range resolve) | 13.42 ns | 2.4× |
| GetByTimeZone spanning US + CA | 18.80 ns | 3.4× |

### `GetBatch` vs sequential

| Size | Sequential | CodeBatch | Batch overhead |
|---|---:|---:|---:|
| 10 codes | 305 ns | 436 ns | 1.43× |
| 100 codes | 3,247 ns | 4,286 ns | 1.32× |
| 1,000 codes | 31,584 ns | 43,093 ns | 1.37× |

---

## Known performance characteristics

### `GetByCode` miss path

Miss lookups allocate 104 B and take ~32 ns — significantly slower than hits (6 ns, 0 B).
The miss path runs the full normaliser chain (normalise → validate → range fallback) before
returning null. Hits short-circuit at the first `FrozenDictionary` probe.

### CA construction time (CSV)

The Canadian dataset dominates CSV registry construction at **~27.5 s** (412k compressed
entries covering ~900k distinct codes, 563 MB). This is a one-time startup cost, but it is
the strongest case for shipping the ZP frozen image: `ca.zpi.br` loads the same data in
**~40 ms** (~690× faster, 20 MB). For the CSV path, pre-warming the registry in a background
task at startup is the recommended mitigation for cold-start-sensitive deployments.

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
