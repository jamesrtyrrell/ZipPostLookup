# ZipPostLookup.CountryDataTools

Developer CLI for preparing, validating, enriching, and exporting country postal code data
for inclusion in [ZipPostLookup](../README.MD).

> **Not part of the NuGet package.** This tool runs against `net10.0` only and is intended
> for contributors maintaining or adding country data files. This is the Machine that builds the machine.
> Creating the curated data files that ZipPostLookup loads is arguably more complicated than ZipPostLookup
> itself, and requires a deep understanding of postal code data formats, data quality, and data privacy.

---
## Sources of Data
```
Sources are primarily from the OpenStreetMap project, and GeoNames. Verification takes 
place via some API calls, or by manual inspection of the source data.
```
---
## Data Architecture

CountryDataTools owns the source of truth for all postal code data.

```
Source CSVs (GeoNames, OSM, etc.)
        ↓  ingest candidate
  data.reference  (SQL working DB)
        ↓  export --target ref
ZipPostLookup.CountryDataTools/Data/{cc}/{cc}.csv.tar.gz  ← ReferenceDataCSV — full data,
        |                                                    gzip-compressed source of truth
        ↓  export  (--target main, default)
ZipPostLookup/Data/{cc}/{cc}.csv                         ← ZipPostLookupDataCSV — compacted,
        |                                                    indexed, ready for BuiltInDataSource
        ↓  export --target zpi   (or --target zpi --from-csv, offline from the CSV above)
ZipPostLookup/Data/{cc}/{cc}.zpi.br                      ← ZP frozen image — binary MPHF blob,
                                                            Brotli-compressed, fast zero-parse load
```

`CountryDataTools/Data` is the git-versioned backup of the database. The reference CSVs are large
(CA ~120&#160;MB raw) so they are stored **gzip-compressed** as `{cc}.csv.tar.gz` (~9&#160;MB for CA)
— under GitHub's 100&#160;MB limit, no Git LFS needed. If the DB is ever lost,
`ingest ref --country XX` reads `CountryDataTools/Data/{cc}/{cc}.csv.tar.gz` from disk, expands it
in-memory (the `tar -xvzf` equivalent), and loads `data.reference` — no data loss, no re-running the
curation pipeline. The raw uncompressed `{cc}.csv` is gitignored (regenerate via `export --target ref`).

`ZipPostLookup/Data` files are build artifacts, regenerated from the source of truth
before each release.

---

## Prerequisites

- .NET 10 SDK
- Docker Desktop (for the working database)
- SQL Server Management Studio 22 (SSMS) — optional but recommended for discrepancy review

---

## Setup

### 1. Start the working database

The pipeline uses a SQL Server working database for storing reference data, candidate imports,
discrepancies, and the full audit trail. The easiest way to run it locally is via Docker:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name zippostlookup-db --hostname sql1 \
  -d mcr.microsoft.com/mssql/server:2022-latest
```

### 2. Run the schema script

Open SSMS, connect to `localhost,1433` with SQL Server Authentication (`sa` / your password),
and run:

```
ZipPostLookup.CountryDataTools/Database/Sql/zippostlookup_workdb_sqlserver.sql
```

This creates the `ZipPostLookupWorkDB` database with all schemas, tables, indexes, views,
and TVP types in one pass. Safe to re-run — all objects are guarded with `IF NOT EXISTS`.

### 3. Initialise the working config

From the solution root, run:

```bash
dotnet run --project ZipPostLookup.CountryDataTools -- db init \
  --country US \
  --connection "Server=localhost,1433;Database=ZipPostLookupWorkDB;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true"
```

This creates `workdb.json` in the current directory. **Add `workdb.json` to `.gitignore`** —
it contains your connection string and must never be committed.

---

## Commands

All commands follow the pattern:

```
dotnet run --project ZipPostLookup.CountryDataTools -- <command> [subcommand] [options]
```

### `db` — Manage the working database

```bash
db init    --country XX --connection "Server=..."   # create workdb.json
db status                                           # show config + recent runs
db newrun  --source <file>                          # create a new run
db test                                             # verify connection only
```

`db` is also accessible via the legacy alias `workdb`.

### `ingest` — Load data into the working database

```bash
ingest ref --country US              # seed data.country_info + data.reference
ingest ref --all                     # seed all known countries
ingest ref --country US --force      # re-import, clearing existing reference rows
ingest ref --country US --info-only  # seed country_info metadata only (no reference rows)

ingest candidate <file.csv> --country US              # import a candidate CSV
ingest coords --source <file.csv> [--country XX]      # bulk-resolve timezones from lat/lng CSV
              [--batch N] [--dry-run]
```

**`ingest ref`** loads `CountryDataTools/Data/{cc}/{cc}.csv` (the ReferenceDataCSV source of
truth) into `data.reference`. Run this once per country before importing candidates, and again
whenever you need to restore the DB from the committed data.

**`ingest candidate`** compares every row in the candidate CSV against `data.reference`
and routes each row to one of:
- **New code** → inserted into `data.reference` with `TimezoneChecked=0, NameChecked=0`
- **Exact match** → marked clean
- **Timezone differs, reference verified** → auto-rejected, logged in `pipeline.decisions`
- **Timezone differs, reference unverified** → `codes.discrepancies` for review
- **Name not found** → `codes.discrepancies` for review

**`ingest coords`** reads a `CODE,LAT,LNG` or `CODE,CITY,STATE,LAT,LNG` CSV and resolves
IANA timezones via GeoTimeZone (no API calls). Rows already having `TimezoneChecked=1`
are skipped. If the CSV includes city names, matching cities get `NameChecked=1` too.

### `validate` — Validate and fix a candidate CSV

```bash
validate <file.csv> --country US                    # interactive guided flow
validate <file.csv> --country US --no-prompts       # non-interactive (CI/scripting)
validate <file.csv> --country US --report out.txt   # custom report path
```

Runs three steps in sequence with prompts between each:

1. **Validate** — parse and report errors, fixable issues, and warnings; writes `{file}.validation.txt`
2. **Fix** *(prompted)* — auto-fix whitespace, booleans, title-casing, timezones, duplicates
3. **Extract** *(prompted)* — remove structurally broken rows; destination prompt offers:
- `(1) DB only` — insert into `codes.candidate` with `status=error` (requires `workdb.json`)
- `(2) File only` — write to `{basename}-extracted-records.csv` for manual review
- `(3) Both`

Pass `--no-prompts` to skip all prompts and run all steps automatically (defaults to file-only extract).

Enter `i` at any prompt to see a description of what that step will do before committing.

### `fix` — Auto-fix a candidate CSV

```bash
fix <file.csv> --country US             # fix in place
fix <file.csv> --country US --out fixed.csv
```

Resolves `Fixable` issues automatically: whitespace, boolean normalisation, title-casing,
timezone conversion from Windows names to IANA, and duplicate removal. Blocks on structural
`Error` issues — those must be extracted first. Writes a post-fix `.validation.txt` report.

### `extractissues` — Remove structurally broken rows

```bash
extractissues <file.csv> --country US
extractissues <file.csv> --country US --out broken.csv
```

Finds all rows with at least one `Severity.Error` finding, writes them to a sidecar file
(default: `{basename}-extracted-records.csv`), removes them from the source, and
re-validates. Fixable issues are left in place for `fix` to resolve.

### `enrich` — Enrich discrepancies or reference data

```bash
enrich candidates --country US --limit 500           # resolve discrepancies via Zippopotam.us
enrich candidates --country US --run run_ID --limit 100 --dry-run

enrich ref --source <coords.csv> [--country XX]      # enrich reference from lat/lng CSV
           [--batch N] [--dry-run]
enrich ref --provider zippopotam --country US        # enrich reference via API
           [--limit 100]
```

**`enrich candidates`** queries Zippopotam.us for each unresolved discrepancy code, resolves
name, state, and timezone, and writes authoritative values back to `codes.discrepancies` and
`data.reference`. Processes `--limit` codes per run (default 100) with a 5s delay between
API calls. Results are committed every 10 codes so a crash loses at most one checkpoint.

- Armed Forces codes (AA/AE/AP) are resolved without an API call — theatre timezones assigned directly.
- 404 responses mark the candidate row as `unfound`.
- State codes are resolved via embedded `us_info.json` — handles ANSI numeric codes, USPS codes, and full names.

**`enrich ref --source`** bulk-updates `data.reference` timezone and name flags from a coordinates file.

### `export` — Export reference data to CSV

Two export targets serving different purposes:

```bash
# Source of truth — full ReferenceDataCSV, gzip-compressed to CountryDataTools/Data/{cc}/{cc}.csv.tar.gz
export --country CA --target ref
export --country CA --target ref --curated-only
export --country CA --target ref --output /custom/path/ca.csv   # plain .csv (uncompressed)
export --target ref --all --curated-only                        # ref backup for US, CA, and MX in sequence

# Release build — compacted ZipPostLookupDataCSV written to ZipPostLookup/Data/{cc}/{cc}.csv
export --country CA
export --country CA --curated-only
export --country CA --output /custom/path/ca.csv
```

**`--target ref`** exports all columns from `data.reference` including `TimezoneChecked` and
`NameChecked`, preserving the full curation state. No pipeline transformation is applied. The output
is a gzip-compressed tar (`{cc}.csv.tar.gz`) by default — the raw CSVs are too large for plain git
(CA ~120&#160;MB); pass `--output …{cc}.csv` for an uncompressed file. Commit the `.tar.gz` after every
curation session — it is the source of truth and the input for
`ingest ref` if the database ever needs to be rebuilt.

**`--target main`** (default) exports an optimised subset for `BuiltInDataSource`. For CA, US,
and MX the full pipeline is applied automatically:
1. Lat/lng columns stripped when coverage is below the 20% threshold
2. Homogeneous prefix groups collapsed to range rows (e.g. `T0A0**:T0A9**`)
3. Timezone and admin1 strings replaced with integer indices
4. A `#meta:` header line written so `BuiltInDataSource` can decode the file

After a main export, rebuild the `ZipPostLookup` project to embed the updated data.

**`--target zpi`** exports the same optimised data as a ZP frozen image (binary minimal-perfect-hash
blob) to `ZipPostLookup/Data/{cc}/{cc}.zpi.br`. `--uncompressed` writes the raw `.zpi` instead.

```bash
# From the database (same data path as --target main)
export --country CA --target zpi

# Offline rebuild from the committed optimised CSV — no Docker/SQL required
export --country CA --target zpi --from-csv
export --country CA --target zpi --from-csv /custom/path/ca.csv
```

**`--from-csv`** rebuilds the image from an already-optimised `{cc}.csv` (defaulting to the committed
`ZipPostLookup/Data/{cc}/{cc}.csv`) instead of querying the working database. The optimisation
pipeline is **not** re-run — the CSV is consumed verbatim (range rows preserved, `#meta:` tz/admin
index tables decoded), so the image mirrors exactly what `ZipPostRegistry` loads and stays in parity
with it. Use it to regenerate the image after an on-disk **format** change when the underlying data
is unchanged and spinning up the database is unnecessary.

### `snapshot` — Full export pipeline for all countries

```bash
snapshot
```

Runs the full export pipeline for US, CA, and MX in sequence with a Y/N confirmation prompt:

1. `export --target ref --all --curated-only` — backs up source-of-truth reference CSVs to `CountryDataTools/Data/{cc}/`
2. `export --all --curated-only` — exports optimised main CSV + ZPI to `ZipPostLookup/Data/{cc}/`

Stops immediately on the first failure and prints targeted recovery commands. On success, prints the recommended next steps: integrity checks, `dotnet build`, `dotnet test`, and git commit.

Use `snapshot` as the standard end-of-session command after curating data, instead of running the export steps manually. See `PROJECTS.md` for the planned full scope (integrity, build, test, and git commit will be added in a future iteration).

### `analyse` — Analyse curated reference data

```bash
analyse --country CA
analyse --country CA --output report.md
```

Analyses `data.reference` for the given country (must have `DataCurated=1`) and writes a
structured Markdown report to `DataAnalysis/{cc}-analysis-{date}.md`. The report covers:

- Dataset overview: row counts, unique codes, names, timezones, admin1 breakdown
- Size estimates: raw CSV, stripped, indexed, Brotli-compressed
- Compression opportunities: homogeneous code groups, range candidates
- Findings with AI prompts for each optimisation opportunity

---

## Typical Workflow — Adding or Updating a Country

### First-time setup for a new country

```bash
# 1. Initialise the DB connection
dotnet run --project ZipPostLookup.CountryDataTools -- db init \
  --country GB --connection "Server=localhost,1433;..."

# 2. Seed country metadata only (no reference data yet)
dotnet run --project ZipPostLookup.CountryDataTools -- ingest ref \
  --country GB --info-only

# 3. Validate and fix the candidate CSV
dotnet run --project ZipPostLookup.CountryDataTools -- validate \
  gb-candidate.csv --country GB

# 4. Import the candidate
dotnet run --project ZipPostLookup.CountryDataTools -- ingest candidate \
  gb-candidate.csv --country GB

# 5. Enrich discrepancies (repeat until none remain)
dotnet run --project ZipPostLookup.CountryDataTools -- enrich candidates \
  --country GB --limit 500

# 6. Export source of truth → ZipPostLookup.CountryDataTools/Data/gb/gb.csv
dotnet run --project ZipPostLookup.CountryDataTools -- export \
  --country GB --target ref --curated-only

# 7. Export release build → ZipPostLookup/Data/gb/gb.csv
dotnet run --project ZipPostLookup.CountryDataTools -- export \
  --country GB --curated-only

# 8. Rebuild ZipPostLookup to embed the updated data, then commit both files
```

### Updating an existing country (e.g. new candidate CSV for CA)

```bash
# 1. Check DB status
dotnet run --project ZipPostLookup.CountryDataTools -- db status

# 2. Validate the new candidate
dotnet run --project ZipPostLookup.CountryDataTools -- validate \
  ca-new-candidate.csv --country CA

# 3. Import — conflicts against the curated reference will be auto-resolved
#    or routed to discrepancies
dotnet run --project ZipPostLookup.CountryDataTools -- ingest candidate \
  ca-new-candidate.csv --country CA

# 4. Check what needs review
# SELECT * FROM pipeline.v_pending_discrepancies WHERE CountryId = 'CA';

# 5. Enrich remaining discrepancies
dotnet run --project ZipPostLookup.CountryDataTools -- enrich candidates \
  --country CA --limit 500

# 6. Export source of truth → ZipPostLookup.CountryDataTools/Data/ca/ca.csv
dotnet run --project ZipPostLookup.CountryDataTools -- export \
  --country CA --target ref --curated-only

# 7. Export release build → ZipPostLookup/Data/ca/ca.csv
dotnet run --project ZipPostLookup.CountryDataTools -- export \
  --country CA --curated-only

# 8. Rebuild ZipPostLookup, commit both exported files
```

### Restoring the database from the source of truth

If the working DB is lost or you're setting up on a new machine:

```bash
# 1. Run the schema script in SSMS (see Setup above)

# 2. Initialise workdb.json
dotnet run --project ZipPostLookup.CountryDataTools -- db init \
  --country CA --connection "Server=..."

# 3. Load the committed source of truth back into data.reference
dotnet run --project ZipPostLookup.CountryDataTools -- ingest ref --country CA

# Full curation state is restored — no re-enrichment needed
```

---

## Database Schema

The working database (`ZipPostLookupWorkDB`) has three schemas:

| Schema     | Tables                                             | Purpose                            |
|------------|----------------------------------------------------|------------------------------------|
| `data`     | `country_info`, `timezones`, `states`, `reference` | Static reference data, loaded once |
| `codes`    | `candidate`, `validation_errors`, `discrepancies`  | Pipeline working data per run      |
| `pipeline` | `runs`, `decisions`                                | Provenance and audit trail         |

### Key views

```sql
-- All unresolved discrepancies with curation status of the reference side
SELECT * FROM pipeline.v_pending_discrepancies WHERE CountryId = 'CA';

-- Progress dashboard — counts per country across all pipeline stages
SELECT * FROM pipeline.v_country_summary;

-- Triage helper — discrepancy counts by field
SELECT * FROM pipeline.v_discrepancy_field_summary WHERE CountryId = 'CA';

-- Coverage — which codes are reference-only, candidate-only, or both
SELECT * FROM pipeline.v_zip_coverage WHERE CountryId = 'CA';
```

### Curation flags on `data.reference`

Each row in `data.reference` carries two quality flags:

| Column             | Meaning                                                          |
|--------------------|------------------------------------------------------------------|
| `TimezoneChecked`  | Timezone verified via GeoTimeZone coordinates or Zippopotam.us  |
| `NameChecked`      | Place name verified via coordinate CSV match or Zippopotam.us   |
| `Curated`          | Computed: `1` when both flags are `1` — fully verified row      |

When `data.country_info.DataCurated = 1` for a country, the reference data is considered
authoritative — incoming candidate conflicts with curated rows are auto-rejected.

---

## workdb.json

Created by `db init`, this file configures the working database connection for the current
session. It lives in the solution root and is walked up to automatically by all commands.

```json
{
  "provider": "sqlserver",
  "connectionString": "Server=localhost,1433;Database=ZipPostLookupWorkDB;...",
  "countryCode": "CA",
  "activeRunId": "run_20260601_001"
}
```

> **Never commit `workdb.json`.** Add it to `.gitignore`.