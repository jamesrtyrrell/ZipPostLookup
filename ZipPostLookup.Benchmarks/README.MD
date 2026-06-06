# ZipPostLookup.Benchmarks

BenchmarkDotNet harness for measuring the performance of `ZipPostLookup` against
[Zip2City](https://github.com/ShereSoft/Zip2City) (ShereSoft).

> **⚠ This project is not part of the NuGet package.**
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
dotnet run -c Release --project ZipPostLookup.Benchmarks --filter "*LookupBenchmarks*"
dotnet run -c Release --project ZipPostLookup.Benchmarks --filter "*ConstructionBenchmarks*"

# Run a specific method
dotnet run -c Release --project ZipPostLookup.Benchmarks --filter "*GetByZip_Hit*"
```

Results are written to:

```
ZipPostLookup.Benchmarks/BenchmarkDotNet.Artifacts/results/
```

---

## Benchmark classes

### `LookupBenchmarks`

Hot-path lookup benchmarks. Both registries are fully warmed up in `[GlobalSetup]` before
any measurement begins, so construction cost does not pollute the results.

| Benchmark         | What it measures                             |
|-------------------|----------------------------------------------|
| `GetByZip` — hit  | Single lookup for a zip that exists          |
| `GetByZip` — miss | Single lookup for a zip that does not exist  |
| `GetAllByZip`     | All city entries for a multi-city zip code   |
| `GetClosest`      | Nearest zip when no exact match exists       |
| `GetByCity`       | Reverse lookup by city name                  |
| `GetByState`      | Reverse lookup by state abbreviation         |
| `GetByTimeZone`   | Reverse lookup by IANA timezone identifier   |
| 10× spread        | 10 `GetByZip` calls across the full keyspace |
| 5× miss spread    | 5 `GetByZip` calls for zips that don't exist |

`GetByCity`, `GetByState`, and `GetByTimeZone` have no Zip2City equivalent and are shown
for reference only.

### `ConstructionBenchmarks`

Measures the one-time cost of building each registry from its embedded CSV data. This is
the cost paid at application startup — Lambda cold-start, unit test fixture setup, etc.

| Benchmark            | What it measures                                              |
|----------------------|---------------------------------------------------------------|
| Build US registry    | Parse and index 57k rows into the `FrozenDictionary` indexes  |
| Build CA registry    | Parse and index 412k rows (FSA-compressed from 908k)          |
| Build MX registry    | Parse and index 145k rows                                     |
| Build US+CA+MX       | Parse and index all three into a single combined registry     |
| Load ZP image US/CA/MX | Brotli-decompress + map the `{cc}.zpi.br` frozen image      |
| Zip2City type init   | Force Zip2City's static constructor (compile-time baked data) |

### `ZpImageBenchmarks` and `ClosestImageBenchmarks` — CSV vs ZP frozen image

Compare the CSV-built `ZipPostRegistry` against the binary `ZpImageLookup` (`{cc}.zpi.br`) for the
**same data across US, CA, and MX**. `ZpImageBenchmarks` parameterises over the three countries and
pairs every operation as a CSV (baseline) + ZPI method within a `[BenchmarkCategory]`, so the `Ratio`
column reads as *ZPI relative to CSV* per operation per country. `ClosestImageBenchmarks` covers
`GetClosest` for US and MX only (CA codes are alphanumeric and `GetClosest` rejects them). Both run a
CSV-vs-ZPI parity guard in `[GlobalSetup]` so a mismatch fails the run rather than silently skewing
numbers.

```bash
dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter "*ZpImageBenchmarks*" "*ClosestImageBenchmarks*" "*ConstructionBenchmarks*"
```

Full run instructions, result-table scaffolding, and AI analysis prompts live in
[ZpiVsCsvBenchmark.MD](../ZpiVsCsvBenchmark.MD).

### `InternationalLookupBenchmarks` — CSV hot-path across all three countries

Single-backend (CSV) lookup benchmarks covering US, CA, and MX individually plus the combined
US+CA+MX multi-country registry. Focuses on demonstrating international capabilities that have no
Zip2City equivalent: CA range resolution, MX estado lookup, and cross-country timezone queries.

### `CaRangeBenchmarks` — CA FSA range compression deep-dive

Isolates the CA range index cost: exact LDU hits vs compressed-FSA hits, miss paths, and input
normalisation overhead (lowercase, spaced, hyphenated). Confirms that range resolution adds
negligible overhead (~0 ns vs a plain exact hit for in-range codes).

### `BatchLookupBenchmarks` — `GetBatch` vs sequential loop

Compares `GetBatch(CodeBatch)` against an equivalent `for` loop of `GetByCode` calls at sizes
10, 100, and 1,000 codes. Also measures the deduplication benefit (200 codes, 50% duplicates)
and `CodeBatch` construction cost (single-threaded vs concurrent).

---

## GetBatch usage

`GetBatch` looks up multiple postal codes in one call and returns an `IReadOnlyDictionary<string, ZipEntry?>`.

```csharp
// Pre-build the batch once — reuse it across requests
var batch = new ZipCodeBatch()
    .Add("10001")
    .Add("90210")
    .Add("10001")   // silently deduped
    .AddRange(codesFromCsv);

var results = ZipPostLookup.GetBatch(batch);

foreach (var (code, entry) in results)
    Console.WriteLine(entry != null ? $"{code} → {entry.Name}" : $"{code} → not found");

// Convenience overload — duplicates silently ignored
var results2 = ZipPostLookup.GetBatch(new[] { "10001", "90210", "10001" });

// Multi-threaded batch building
var concurrent = new ZipCodeBatch(concurrent: true);
Parallel.ForEach(myData, item => concurrent.Add(item.ZipCode));
var results3 = registry.GetBatch(concurrent);
```

> **Performance note:** `GetBatch` is **slower than a sequential loop** for unique inputs (~1.4× at 100 codes) due to `Dictionary` return value construction overhead. It wins when **duplicates are expected** — 200 codes with 50% duplicates resolve in 100 lookups rather than 200, ~33% faster than sequential. Build the `ZipCodeBatch` once and reuse it across calls.

| Scenario                         | Recommendation                           |
|----------------------------------|------------------------------------------|
| Single code or small unique list | `GetByCode` directly                     |
| Large list of unique codes       | Sequential loop with `GetByCode`         |
| List with expected duplicates    | `GetBatch` with pre-built `ZipCodeBatch` |
| Multi-threaded input building    | `new ZipCodeBatch(concurrent: true)`     |

---

## Interpreting results

BenchmarkDotNet reports `Mean`, `Error`, `StdDev`, `Ratio`, `Gen0`, and `Allocated` for
each method. The most important columns for this library are:

- **Mean** — average execution time in nanoseconds (ns) or milliseconds (ms)
- **Allocated** — managed heap bytes allocated per operation; `—` means zero allocations
- **Ratio** — relative to the baseline (`ZipPostLookup — GetByZip (hit)` = 1.00)

Zero allocation on `GetByZip` hits is a deliberate design goal. `ZipRegistry` returns a
reference to an already-live `ZipEntry` record stored in the `FrozenDictionary` — no new
objects are created on the hot path.

---

## Latest results

Benchmarks run on:

```
Windows 11 (10.0.22631.6199 / 23H2)
13th Gen Intel Core i7-13700K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.300 — Runtime: .NET 8.0.27, X64 RyuJIT AVX2
```

### Hot-path lookups (US, vs Zip2City)

| Method                                    |        Mean |  Ratio | Allocated |
|-------------------------------------------:|------------:|-------:|----------:|
| ZipPostLookup — GetByZip (hit)             |     6.04 ns |   1.00 |       0 B |
| ZipPostLookup — GetByZip (miss)            |    31.59 ns |   5.23 |     104 B |
| ZipPostLookup — GetAllByZip               |     8.63 ns |   1.43 |      24 B |
| ZipPostLookup — GetClosest (no match)     |    36.84 ns |   6.10 |      32 B |
| ZipPostLookup — 10× GetByZip spread       |    67.89 ns |  11.24 |       0 B |
| ZipPostLookup — 5× GetByZip (all miss)    |   109.34 ns |  18.11 |     312 B |
| Zip2City — GetDefaultCityState (hit)      |    33.02 ns |   5.47 |     160 B |
| Zip2City — GetDefaultCityState (miss)     |    29.11 ns |   4.82 |     160 B |
| Zip2City — GetAllCityStates               |    45.70 ns |   7.57 |     160 B |
| Zip2City — GetClosest (no match)          | 1,067.67 ns | 176.81 |     160 B |
| Zip2City — 10× GetDefaultCityState spread |   332.07 ns |  54.99 |  1,600 B  |
| Zip2City — 5× GetDefaultCityState (miss)  |   192.53 ns |  31.88 |     800 B |

> **10× spread:** ZipPostLookup at 67.9 ns vs Zip2City at 332.1 ns — **4.9× faster**, zero allocations vs 1,600 B.

### Reverse lookups (US, no Zip2City equivalent) — CSV registry

| Method            |    Mean | Allocated |
|-------------------|--------:|----------:|
| GetByName / GetByCity |  7.47 ns | 24 B |
| GetByState        |  6.78 ns |     24 B |
| GetByStateName    |  9.61 ns |     24 B |
| GetByTimeZone     |  8.19 ns |     24 B |

> The ZP image (v3 format) ranges from **0.85–2.0× CSV** on reverse lookups and is **allocation-free**
> on every call after the first (per-bucket result caching). Several operations beat CSV outright (GetByAdmin US/MX 0.85/0.93×). See [ZpiVsCsvBenchmark.MD](../ZpiVsCsvBenchmark.MD) §3.1.

### Construction cost (CSV registry build vs ZP image load)

| Method                                  |       Mean | Allocated |
|-----------------------------------------|-----------:|----------:|
| Build US registry (57k rows, curated)   |     184 ms |   82.2 MB |
| Build MX registry (145k rows)           |     608 ms |  188.9 MB |
| Build CA registry (412k rows, FSA-comp) |  14,727 ms |  559.8 MB |
| Build US+CA+MX combined                 |  15,801 ms |  806.9 MB |
| **Load US ZP image (`us.zpi.br`)**      |   **9 ms** |  **11 MB** |
| **Load MX ZP image (`mx.zpi.br`)**      |  **18 ms** |  **22 MB** |
| **Load CA ZP image (`ca.zpi.br`)**      |  **43 ms** |  **53 MB** |
| Zip2City — type initialisation          |   0.033 ms |     160 B |

Construction cost is paid once at startup and never again. Building the CSV registry is ~184 ms for
US and ~14.7 s for CA (its 412k-row dataset dominates). The **ZP frozen image** loads the same data
**20–342× faster** with ~7–10× less memory (Brotli-decompress + map, no per-row construction) — the
single strongest reason to ship the image for cold-start-sensitive deployments. Full breakdown in
[ZpiVsCsvBenchmark.MD](../ZpiVsCsvBenchmark.MD); Zip2City comparison in
[ZipPostvsZip2CityBenchmark.MD](../ZipPostvsZip2CityBenchmark.MD).

---

## Known performance gaps

### `GetByZip` miss path

Miss lookups allocate 104 B and take ~31–39 ns — significantly slower than hits (6 ns, 0 B).
The miss path runs the full normaliser chain (normalise, validate, range fallback) before returning
null. Hits return a reference to an already-live `ZipEntry` in the `FrozenDictionary` — zero allocation.

### CA construction time

The Canadian dataset dominates registry construction at **~14.7 s** (412k compressed entries covering
~900k distinct codes; ~560 MB). This is a one-time startup cost, but it is the strongest case for
shipping the **ZP frozen image**: `ca.zpi.br` loads the same data in **~43 ms** (~342× faster, 53 MB).
For the CSV path, pre-warming the registry in a background task at startup is the recommended
mitigation for Lambda cold-starts or short-lived processes.

---

## Adding a new benchmark

Add a method to an existing class or create a new class in the project. The only rules
BenchmarkDotNet requires are:

- The class must be `public`
- The method must be `public` and decorated with `[Benchmark]`
- Run with `dotnet run -c Release` — not from the IDE run button, which uses Debug

```csharp
[Benchmark(Description = "ZipPostLookup — GetByStateName")]
public IReadOnlyList<ZipEntry> GetByStateName() =>
    _registry.GetByStateName("California");
```

To add a new class, BenchmarkDotNet discovers all `[Benchmark]`-decorated public methods
in the assembly automatically — no registration needed.