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
2026-06-04-ClosestImageBenchmarks-report.csv
```

---

## Benchmark groups

For details on what each benchmark measures, see the [README](../README.MD).

| Benchmark                       | What it measures                                             | Archive file                          |
|---------------------------------|--------------------------------------------------------------|---------------------------------------|
| `LookupBenchmarks`              | Hot-path lookup benchmarks                                   | `LookupBenchmarks.tar.gz`             |
| `ConstructionBenchmarks`        | One-time cost of building each registry                      | `ConstructionBenchmarks.tar.gz`       |
| `ZpImageBenchmarks`             | CSV-built registry vs binary ZP image, all three countries   | `ZpImageBenchmarks.tar.gz`            |
| `ClosestImageBenchmarks`        | `GetClosest` for US and MX only                              | `ClosestImageBenchmarks.tar.gz`       |
| `InternationalLookupBenchmarks` | Single-backend (CSV) lookup benchmarks across US, CA, MX    | `InternationalLookupBenchmarks.tar.gz`|
| `CaRangeBenchmarks`             | Isolates the CA FSA range index lookup cost                  | `CaRangeBenchmarks.tar.gz`            |
| `BatchLookupBenchmarks`         | `GetBatch(CodeBatch)` vs an equivalent `for` loop            | `BatchLookupBenchmarks.tar.gz`        |

---

## Release history

### 1.0.0 RC1 — 2026-06-15

- Initial release of the benchmark suite, covering core lookup and construction costs.
- Baseline comparison against the existing NuGet package Zip2City.
- Expanded to cover performance of the ZPI (binary frozen image) format.

---

## Notes for future releases

When benchmarks are added or removed, or methodology changes significantly between releases,
document the change here so archived results from different periods can be interpreted correctly.
