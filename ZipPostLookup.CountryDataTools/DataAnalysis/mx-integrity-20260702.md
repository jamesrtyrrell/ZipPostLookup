# MX — ZipPostLookup Data Integrity Report

| | |
|---|---|
| **Country** | MX — Mexico |
| **Date** | 2026-07-02 17:44 UTC |
| **Result** | ✅ PASSED |
| **Default codes tested** | 15,000 of 32,896 distinct zip codes (45.6% coverage) |
| **Non-default rows tested** | 15,000 (GetAllByCode name-presence only) |
| **Pass rate** | 100.00% (15,000 / 15,000 default codes clean) |
| **Total failures** | 0 across 0 unique code(s) |

## Per-Method Summary

| Method | Sample | Checks | Failures | Pass Rate |
|---|---|---:|---:|---:|
| GetByCode | Default only | 15,000 | 0 | 100.00% |
| GetByZip | Default only | 15,000 | 0 | 100.00% |
| GetAllByCode (default) | Default only | 15,000 | 0 | 100.00% |
| GetAllByCode (non-default) | Non-default | 15,000 | 0 | 100.00% |

## Failures

No failures detected. All sampled codes returned correct name, timezone, and admin data.

## What Was Checked

Each sampled **default** code was verified against the curated rows in `data.reference`:

- **GetByCode** — name, timezone, admin1 value, admin1 code
- **GetByZip** — same fields (alias parity with GetByCode)
- **GetAllByCode** — default entry present, default entry fields correct

Each sampled **non-default** row was verified separately:

- **GetAllByCode** — the non-default name must appear somewhere in the result list

> For each zip the **representative default row** is the `IsDefault=1` row whose
> `Name` is alphabetically last. This matches `ZipPostRegistry`'s last-write-wins
> index behaviour: the CSV is sorted by zip + name ascending, so the last
> `IsDefault=true` row processed is the alphabetically last name — and that is
> the entry `GetByCode` / `GetByZip` returns. Sampling any other `IsDefault=1`
> row would produce false failures on countries like MX where many zips have
> multiple `IsDefault=1` rows in the database.

> This is a **data integrity** check, not a benchmark or unit test.
> Its purpose is to confirm that the embedded CSV data loaded by ZipPostLookup
> matches the curated source-of-truth in the working database.
