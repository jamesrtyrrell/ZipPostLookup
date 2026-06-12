# ZipPostLookup.Benchmarks — History Manifest

This folder tracks benchmark results for the `ZipPostLookup` library over time. Each benchmark
class has its own `.tar.gz` archive containing dated CSV reports exported by
[BenchmarkDotNet](https://benchmarkdotnet.org/) from `BenchmarkDotNet.Artifacts/results/`.

Results are appended automatically at the end of each benchmark run via a y/n prompt.

---

## Archive format

Each archive contains one CSV per run, named:

```
{yyyy-MM-dd}-{BenchmarkClass}-report.csv
```

For example, `ClosestImageBenchmarks.tar.gz` might contain:

```
2026-05-30-ClosestImageBenchmarks-report.csv
2026-06-08-ClosestImageBenchmarks-report.csv
```

---

## Benchmark groups

For details on what each benchmark measures, see the [README](../README.MD).

| Benchmark | What it measures | Archive file |
|---|---|---|
| `LookupBenchmarks` | Hot-path US lookup vs Zip2City | `LookupBenchmarks.tar.gz` |
| `ConstructionBenchmarks` | One-time cost of building each registry, CSV vs ZPI | `ConstructionBenchmarks.tar.gz` |
| `ZpImageBenchmarks` | CSV-built registry vs binary ZP image — all operations, all countries | `ZpImageBenchmarks.tar.gz` |
| `ClosestImageBenchmarks` | `GetClosest` CSV vs ZPI, US and MX | `ClosestImageBenchmarks.tar.gz` |
| `MultiCountryBenchmarks` | Single combined US+CA+MX registry — overhead per country and operation | `MultiCountryBenchmarks.tar.gz` |
| `LegacyVsModernBenchmarks` | `Dictionary` vs `FrozenDictionary` hot path, with Zip2City reference | `LegacyVsModernBenchmarks.tar.gz` |
| `BatchLookupBenchmarks` | `GetBatch(CodeBatch)` vs sequential `for` loop | `BatchLookupBenchmarks.tar.gz` |

---

## Release history

### CA construction quadratic fix + Brotli-CSV embedding — 2026-06-12 (current)

Two changes reshape `ConstructionBenchmarks` and the embedded-resource story. Lookup hot paths are
unchanged (within noise across the whole suite).

- **CA construction quadratic fix.** `ZipPostRegistry.AddToIndex` used `list.Insert(0, entry)` for
  every default entry — an O(n) memmove that became quadratic on CA's huge reverse-lookup buckets
  (`byAdmin1Code["ON"]`, `byTimeZone["America/Toronto"]`, etc.). It now appends, with one linear
  post-load pass (`ReorderDefaultsFirst`) reproducing the exact previous element order. **CA build:
  32,962 ms → 1,373 ms (24×); US 189 → 136 ms; MX 609 → 375 ms; US+CA+MX 34.2 s → 2.19 s.** Per-row
  build cost is now uniform across countries (~2.3 µs/row for CA, equal to US — the superlinearity is
  gone). Allocations and all lookup paths are unchanged. Full write-up:
  [`CA-CONSTRUCTION-INVESTIGATION.md`](CA-CONSTRUCTION-INVESTIGATION.md).

  > **Comparing across this date:** pre-06-12 CA/combined `ConstructionBenchmarks` Means are not
  > comparable to current — the build algorithm changed. Lookup classes are unaffected.

- **Data regenerated and grown.** CA 412,403 → **603,272** FSA-compressed entries; US 57,400 →
  **59,719** rows; MX 145,826 → **146,183** rows. Distinct codes: US 43,498 · CA ~900,686 · MX 32,893.
  Benchmark `[Description]` row-count labels were updated to match.

- **Brotli-CSV embedding.** The library now embeds the Brotli-compressed `{cc}.csv.br` (the CSV
  backend decompresses at load) alongside `{cc}.zpi.br`, instead of the plain `.csv`. Consequence:
  the `.zpi.br` image is **no longer the smallest artifact on disk** (CA: `ca.csv.br` 784 KB vs
  `ca.u16.zpi.br` 5,882 KB) — the image's win is now load time and startup memory, not footprint.

- **Cold-start comparison reshaped.** Because the CSV build is now fast, the image's load-vs-build
  *time* lead fell from ~340–430× (CA) to ~17–28×; its *memory* lead grew to ~29–31× less allocation.
  CA ZPI load rose to ~55 ms (from ~41 ms) tracking the larger dataset. See
  [`ZpiVsCsvBenchmark.MD`](../../ZpiVsCsvBenchmark.MD) §3.3.

### ZPI v4 fused directory + lazy NamePool — 2026-06-08

Two ZPI reader/format changes from the "Must go faster" project affect `ConstructionBenchmarks`
and the ZPI hot path. **When comparing `ConstructionBenchmarks` runs across this date, note that
the `ZP image — load …` rows changed for reasons unrelated to the machine:**

- **Candidate 1 — v4 fused directory.** Single-record codes (CA 98.8%, US 76%) now inline their
  record's `nameIndex/tz/admin/flags` into the directory entry, skipping the Records-section fetch
  on `GetByCode`. Format version bumped 3 → 4.
- **Candidate 2 — lazy NamePool decode (reader-only, no format change).** The reader no longer
  eager-decodes the entire NamePool into `string[]` at load; `Name(idx)` decodes on first access and
  caches. This cut `ZP image — load` time/allocation, scaling with unique-name count:

  | Country | Names | Load Mean (before → after) | Allocated (before → after) |
  |---|---|---|---|
  | MX | 77,932 | 24.14 ms → 14.29 ms (−40.8%) | 11.29 MB → 6.63 MB (−41.3%) |
  | US | 26,745 | 10.20 ms → 8.11 ms (−20.5%) | 4.14 MB → 2.96 MB (−28.5%) |
  | CA | 7,186 | 39.76 ms → 40.97 ms (flat, Brotli-bound) | 20.44 MB → 20.00 MB (−2.2%) |

  Caveat: this is a deferral — names not touched at load are decoded lazily on first lookup, so
  `Allocated` reflects load-time only.

### 1.0.0 — 2026-06-08

**Breaking changes to benchmark suite:**
- `InternationalLookupBenchmarks` removed — superseded by `MultiCountryBenchmarks`, which runs a
  single combined US+CA+MX registry rather than three separate ones and better reflects real
  multi-country usage.
- `CaRangeBenchmarks` removed — FSA range resolution overhead is now captured in
  `MultiCountryBenchmarks` (`GetByCode CA postal (range resolve)` benchmark) and in
  `ZpImageBenchmarks` (parameterised over CA).

**New benchmark classes:**
- `MultiCountryBenchmarks` — combined US+CA+MX registry, US/CA/MX code lookups, cross-country
  timezone query.
- `LegacyVsModernBenchmarks` — isolates `Dictionary` (netstandard2.0 path) vs `FrozenDictionary`
  (net8.0 path) built from live registry data. Zip2City is included as a reference point.

**Data changes affecting results:**
- `CodeEntry.Lat` and `CodeEntry.Lng` removed (export pipeline strips them below 20% coverage).
  ZPI image sizes reduced significantly: CA from ~86 MB to ~20 MB, US from ~11 MB to ~4 MB.
  Construction times updated accordingly.
- CA data corrected (province codes populated from FSA prefix rules). CA CSV construction time
  increased from ~14.7 s to ~27.5 s; ZPI load unchanged (~40 ms).

**Archive note:**
History archives from runs prior to 2026-06-08 are not retained. Results from this date forward
are preserved. When comparing against archived results, note the lat/lng and CA data changes above —
pre-2026-06-08 ZPI numbers for CA are not comparable to current numbers.

---

## Notes for future releases

When benchmarks are added or removed, or methodology changes significantly between releases,
document the change here so archived results from different periods can be interpreted correctly.
