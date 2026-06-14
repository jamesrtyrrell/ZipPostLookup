# ZipPostLookup — Data Quality & Curation

ZipPostLookup is a fast, extensible, in-memory postal-code lookup library for .NET — but its real focus is **data quality, integrity, and long-term maintainability**.

Rather than simply scraping large volumes of postal codes from the web, the project is built around a **curation pipeline** that validates, normalizes, enriches, and cross-checks data before it is published. A dedicated console toolkit (CountryDataTools) continuously analyzes the working data to detect inconsistencies, malformed codes, duplicate records, impossible ranges, and the other integrity issues that commonly appear in publicly available datasets — so the library ships with data we can stand behind, not merely a large pile of unverified records.

---

## Built-in curated datasets

| Country | Codes / Entries | Status |
|---|---|---|
| United States | 43,501 ZIP codes | Fully curated |
| Canada | ~901,512 postal codes / 621k compressed entries (FSA range compression) | Fully curated |
| Mexico | 32,893 postal codes | Fully curated |

All three datasets are fully curated and validated before each release.

---

## How data flows: ingest → enrich → check → curate → certify → ship

Every postal code travels the same path before it reaches a release:

1. **Ingest** — raw postal data is imported from one or more sources (GeoNames, OpenStreetMap, official postal files, or a flat list of codes). Incoming rows are auto-fixed for obvious issues (whitespace, casing, boolean formats) and validated before they touch the working database.
2. **Enrich** — codes missing a place name, administrative area (state/province), timezone, or coordinates are filled in from a rotating pool of geocoding/postal APIs, with timezone derived from coordinates. Enrichment is rate-limit aware and resumable.
3. **Check** — automated validation and integrity scans flag anything inconsistent, malformed, duplicated, or geographically impossible.
4. **Curate** — a human reviewer confirms records, resolves flagged discrepancies, fixes or flags bad data, and links genuine alternate names.
5. **Gold-certify** — codes that meet every quality condition (see *The Gold Standard* below) are certified and continuously monitored for regressions.
6. **Export & integrity-test** — validated data is compressed into the shipped CSV and binary image, then a random sample is looked up *through the actual shipped library* and compared back to the source database to confirm the published artifact matches the source of truth — before anything is packaged to NuGet.

---

## Safeguards

These are the protections that stop bad data from ever reaching a release:

- **Coordinate pairing rule** — latitude and longitude are only ever stored as a *complete pair*. A half-populated row (a latitude with no longitude, or vice-versa) is never written, enriched, or exported — both values are blanked together if either is missing. This rule is enforced at every write point.
- **Deprecated-timezone canonicalization** — retired IANA timezone IDs (for example `America/Nipigon` → `America/Toronto`) are mapped to their current canonical equivalents wherever a timezone is resolved from coordinates. The mapping lives in per-country rules, not scattered through the code.
- **Special-domain code handling** — military (APO/FPO/DPO), U.S. territories, and PRS codes are recognized and handled deliberately: they aren't mis-flagged as "new" codes, aren't sent to geocoding APIs that would always fail for them, and Armed Forces codes receive their theatre timezone directly.
- **Bad-actor suppression** — codes a curator flags as bad (widely-shared fakes, junk) are excluded from the shipped data while being retained in the reference set as a historical record.
- **Alias awareness** — abbreviation-equivalent place names (e.g. "St. Martin" / "Saint Martin", "Ft Worth" / "Fort Worth") are linked as alternates rather than treated as conflicts. When an incoming source disagrees with a *certified* code's verified name, it is annotated as a possible real alias for review rather than silently overwritten.
- **Curated-only releases** — only human-verified rows are exported into the shipped library; uncurated working rows never ship.
- **Implausible-value guard** — enrichment rejects garbage API responses (such as absurdly long place names) and logs them instead of writing them into the data.
- **Rate-limit-aware enrichment** — lookups rotate across multiple providers; any provider that returns "too many requests" is dropped for the rest of the run, and daily/monthly usage is tracked per provider.
- **Resumable, transactional enrichment** — work is checkpointed in small transactions and can be interrupted and resumed without corrupting a run.
- **Internal data-access discipline** — all SQL is centralized and all writes go through a single service layer, preventing query/column drift between the bulk paths and the read paths over time.

---

## Data validation checks

**At import (per-file, before data enters the working database):**

- Required header columns are present.
- Required fields (postal code, place name, timezone) are non-empty.
- The postal code matches the country's expected format — five digits for the U.S. and Mexico; a letter-digit-letter prefix (FSA) for Canada.
- Coordinates are present as a valid pair and fall within plausible latitude/longitude ranges.
- The timezone is a real IANA zone (and not a known deprecated alias).
- The `IsDefault` flag is a normalized boolean, and **exactly one** default (primary) row exists per code.
- Duplicate rows are detected and collapsed.

**On the working database (eleven independent integrity probes, run any time):**

1. **Admin-code correctness** — the stored state/province code matches what the country's structure says it should be (U.S. and Mexico, which derive deterministically from the code).
2. **Missing administrative area** — curated codes with a null, blank, or all-numeric admin code.
3. **Orphan alternate names** — alt-name rows that point to a canonical name that doesn't exist.
4. **Duplicate primaries** — codes with more than one row marked as the default.
5. **Invalid code format** — rows whose code doesn't match the country format.
6. **No primary row** — curated codes with no authoritative default, so a lookup has no primary answer.
7. **Alt-name marked as primary** — an alternate name incorrectly flagged as the default result.
8. **Blank place names** — curated rows that would export an empty name.
9. **Falsely-verified timezones** — rows marked timezone-checked but carrying a blank/placeholder timezone.
10. **Gold regressions** — certified codes that no longer meet every gold condition.
11. **Open gold name discrepancies** — certified codes whose verified name is contradicted by an incoming source (a possible real alias to promote).

**On the shipped artifact (after every export):**

- A random sample of curated codes is looked up **through the published library** — both the text dataset and the compressed binary image — and the returned place name, timezone, and administrative area are compared back to the source database.
- A parity test guarantees the text dataset and the binary image return byte-identical results for the same code.

---

## The Gold Standard

"Gold" is the project's mark of a fully-trustworthy postal code. A code is **certified Gold only when every one of these conditions holds** across all of its curated, non-flagged rows:

1. **It has a curated primary row** — a human-verified default (authoritative) entry exists for the code.
2. **Every row has a valid administrative area** — a present, non-blank, properly-formatted state/province code (not a raw number, not a placeholder).
3. **Every row has a real timezone** — no blanks or placeholders.
4. **At least one row has real coordinates** — a genuine latitude/longitude pair.

Certified codes are tracked separately, stamped with the version of the checks they passed, and **re-certified idempotently** (re-running is always safe). They are also **continuously monitored for regression**: if a later edit breaks any of the four conditions, the integrity scan surfaces the code so it can be re-curated or revoked. In short, Gold means a code has been verified to have a correct primary place name, a valid region, a valid timezone, and a real location — and is being watched to make sure it stays that way.

---

### Last updated

2026-06-13
