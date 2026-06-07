# CDT Console — Screen Reference

Reference for the CountryDataTools interactive dashboard (`countrydatatools dashboard`).
Organised by the breadcrumb path shown in the title bar so that naming any path (e.g. `ZipPostLookup › ZpCode Editor › MX › 06600`) unambiguously identifies the screen, its files, and its queries.

**Not committed to git.**

---

## Quick-reference index

| Breadcrumb | Primary file | Method |
|---|---|---|
| `ZipPostLookup › Dashboard` | `Dashboard/DashboardCommand.cs` | `RunAsync` |
| `ZipPostLookup › Ingest` | `Dashboard/IngestDashboard.cs` | `RunAsync` |
| `ZipPostLookup › Ingest › ref` | `Dashboard/IngestDashboard.cs` | `RunRefAsync` |
| `ZipPostLookup › Ingest › candidate` | `Dashboard/IngestDashboard.cs` | `RunCandidateAsync` |
| `ZipPostLookup › Ingest › coords` | `Dashboard/IngestDashboard.cs` | `RunCoordsAsync` |
| `ZipPostLookup › Validate` | `Dashboard/ValidateDashboard.cs` | `RunAsync` |
| `ZipPostLookup › Enrich` | `Dashboard/EnrichDashboard.cs` | `RunAsync` |
| `ZipPostLookup › Enrich › candidates` | `Dashboard/EnrichDashboard.cs` | inner loop |
| `ZipPostLookup › Enrich › direct` | `Dashboard/EnrichDashboard.cs` | inner loop |
| `ZipPostLookup › Enrich › ref` | `Dashboard/EnrichDashboard.cs` | shows help only |
| `ZipPostLookup › Export` | `Dashboard/ExportDashboard.cs` | `RunAsync` |
| `ZipPostLookup › Export › ref` | `Dashboard/ExportDashboard.cs` | inner loop |
| `ZipPostLookup › Export › main` | `Dashboard/ExportDashboard.cs` | inner loop |
| `ZipPostLookup › Export › zpi` | `Dashboard/ExportDashboard.cs` | inner loop |
| `ZipPostLookup › Analyse` | `Dashboard/AnalyseDashboard.cs` | `RunAsync` |
| `ZipPostLookup › Integrity` | `Dashboard/IntegrityDashboard.cs` | `RunAsync` |
| `ZipPostLookup › Integrity › {CC}` | `Dashboard/IntegrityDashboard.cs` | single-country branch |
| `ZipPostLookup › Integrity › All` | `Dashboard/IntegrityDashboard.cs` | `RunAllAndBrowseAsync` |
| `ZipPostLookup › Convert` | `Dashboard/ConvertDashboard.cs` | `RunAsync` |
| `ZipPostLookup › Snapshot` | `Dashboard/SnapshotDashboard.cs` | `RunAsync` |
| `ZipPostLookup › DB` | `Dashboard/DbDashboard.cs` | `RunAsync` |
| `ZipPostLookup › DB › status` | `Dashboard/DbDashboard.cs` | `RunSimpleAsync("status")` |
| `ZipPostLookup › DB › test` | `Dashboard/DbDashboard.cs` | `RunSimpleAsync("test")` |
| `ZipPostLookup › DB › init` | `Dashboard/DbDashboard.cs` | `RunInitAsync` |
| `ZipPostLookup › DB › newrun` | `Dashboard/DbDashboard.cs` | `RunNewRunAsync` |
| `ZipPostLookup › DB › normalize-tz` | `Dashboard/DbDashboard.cs` | `RunSimpleAsync("normalize-tz")` |
| `ZipPostLookup › DB › normalize-names` | `Dashboard/DbDashboard.cs` | `RunNormalizeNamesAsync` |
| `ZipPostLookup › DB › clear` | `Dashboard/DbDashboard.cs` | `RunWithCountryAsync("clear")` |
| `ZipPostLookup › DB › reset` | `Dashboard/DbDashboard.cs` | `RunWithCountryAsync("reset")` |
| `ZipPostLookup › ZpCode Editor` | `Dashboard/ZpCodeEditorDashboard.cs` | `RunAsync` |
| `ZipPostLookup › ZpCode Editor › {CC}` | `Dashboard/ZpCodeEditorDashboard.cs` | `BrowseCodesAsync` |
| `ZipPostLookup › ZpCode Editor › {CC} › {ZpCode}` | `Dashboard/ZpCodeEditorDashboard.cs` | `EditCodeAsync` |
| `ZipPostLookup › ZpCode Editor › {CC} › {ZpCode} › Edit Timezone` | `Dashboard/ZpCodeEditorDashboard.cs` | `PromptTimezoneAsync` |

---

## Shared infrastructure (every screen)

Every screen uses these regardless of breadcrumb depth.

| Class | File | Role |
|---|---|---|
| `DashboardRenderer` | `Dashboard/DashboardRenderer.cs` | `RenderHeader(title)` — clears screen, draws title bar + rule + workdb.json status line |
| `MenuPrompt` | `Dashboard/MenuPrompt.cs` | `Show<T>(choices, converter, escapeReturns)` — ↑↓/Enter/Esc keyboard menu |
| `CommandEntry` | `Dashboard/CommandEntry.cs` | Record: `Name`, `Description`, `ShowHelp`, `IsInteractive` |
| `DashboardStats` | `Dashboard/DashboardStats.cs` | `TryLoadAllAsync()` + `BuildTable()` — all-country stats panel with progress bars |
| `WorkDbContext` | `Database/WorkDb/WorkDbContext.cs` | `LoadAsync(dir)` — walks up tree for workdb.json, tests connection |
| `WorkDbConfig` | `Database/WorkDb/WorkDbConfig.cs` | `FindConfigFile()` / `Load()` — workdb.json DTO |
| `CommonQueries` | `Database/Sql/CommonQueries.cs` | All SQL query strings |

**Status line:** `DashboardRenderer` reads `workdb.json` (file read only, no DB connection) and shows `CC: {cc}  ·  Run: {runId}` as a dim line after the rule on every screen. Silent if no workdb.json found.

---

# ZipPostLookup › Dashboard

The main command menu. Displays all 10 commands on the left alongside an all-country curation stats panel (Tz. Checked / Name Checked / Curated / Total / Remaining / Progress bar + totals row) on the right. Stats load once on entry and refresh after each sub-command returns.

**Keyboard:** ↑↓ move · Enter select · Esc exit to terminal

**Files:**
- `Dashboard/DashboardCommand.cs` — `RunAsync`, `Render`, `BuildMenuTable`
- `Dashboard/DashboardStats.cs` — `TryLoadAllAsync`, `BuildTable`
- `Database/Sql/CommonQueries.cs` — `GetAllCountryCurationStats`

---

# ZipPostLookup › Ingest

Submenu for loading data into the working database. Three modes.

**Files:** `Dashboard/IngestDashboard.cs` — `RunAsync`

## Ingest › ref

Seeds `data.Reference` and `data.CountryInfo` from the embedded library CSV for one country or all three. Prompts for country, `--force` (re-import even if rows exist), `--info-only` (country_info only, skip reference rows).

**Files:**
- `Dashboard/IngestDashboard.cs` — `RunRefAsync`
- `Commands/Handlers/IngestCommand.cs` — `RunAsync(string[] args)`
- `Database/Repositories/SqlReferenceRepository.cs` — `LoadFromEmbeddedCsvAsync`

## Ingest › candidate

Imports a candidate CSV against reference data for a specific country. Prompts for file path and country.

**Files:**
- `Dashboard/IngestDashboard.cs` — `RunCandidateAsync`
- `Commands/Handlers/IngestCommand.cs` — `RunAsync(string[] args)`
- `Database/Repositories/SqlCandidateRepository.cs` — `InsertBatchAsync`

## Ingest › coords

Bulk-resolves timezones for rows in `data.Reference` that have coordinates but no timezone. Prompts for source CSV path, optional country filter, batch size, and dry-run flag.

**Files:**
- `Dashboard/IngestDashboard.cs` — `RunCoordsAsync`
- `Commands/Handlers/IngestCommand.cs` — `RunAsync(string[] args)`

---

# ZipPostLookup › Validate

Validates a candidate CSV and guides through fix/extract steps. Prompts for file path (blank = back to menu) then country. Option: `--no-prompts` applies fix + extract automatically without per-issue confirmation.

**Files:**
- `Dashboard/ValidateDashboard.cs` — `RunAsync`
- `Commands/Handlers/ValidateCommand.cs` — `RunAsync(string[] args)`

---

# ZipPostLookup › Enrich

Submenu for enriching reference data via the API pool. Three modes.

**Files:** `Dashboard/EnrichDashboard.cs` — `RunAsync`

## Enrich › candidates

Resolves discrepancies in a pipeline run by calling the enrichment API pool. Prompts for country, limit (codes per run), and dry-run flag.

**Files:**
- `Dashboard/EnrichDashboard.cs` — inner loop, `selected == Candidates`
- `Commands/Handlers/EnrichCommand.cs` — `RunAsync(["candidates", ...])`
- `Enrichment/Api/` — `ZippopotamusApi`, `ZiptasticApi`, `EnrichmentApiFactory`

## Enrich › direct

Backfills uncurated `data.Reference` rows directly (not pipeline-bound, no run ID required). Same prompts as candidates.

**Files:**
- `Dashboard/EnrichDashboard.cs` — inner loop, `selected == Direct`
- `Commands/Handlers/EnrichCommand.cs` — `RunAsync(["direct", ...])`

## Enrich › ref

Complex argument shape (file path or API provider); shows CLI help text only. Use `countrydatatools enrich ref` directly for this mode.

**Files:**
- `Dashboard/EnrichDashboard.cs` — shows help, no interactive args
- `Commands/Handlers/EnrichCommand.cs` — called with `["-h"]`

---

# ZipPostLookup › Export

Submenu for exporting reference data. Three targets.

**Files:** `Dashboard/ExportDashboard.cs` — `RunAsync`

## Export › ref

Exports the source-of-truth reference CSV to `CountryDataTools/Data/{cc}/`. After export, auto-updates `{cc}_info.json` and `countries.json` with CodeCount, Curated, CurationStatus, and per-division ZipCount/NameCount. Options: country, `--curated-only`.

**Files:**
- `Dashboard/ExportDashboard.cs` — inner loop, `TargetRef`
- `Commands/Handlers/ExportReferenceCommand.cs` — `RunMainExportAsync`, `UpdateCountryMetadataAsync`
- `Database/Sql/CommonQueries.cs` — `GetAdminDivisionCounts`

## Export › main

Exports optimised library CSV (range-compressed, AltNameOf rows excluded) to `ZipPostLookup/Data/{cc}/`. Options: country, `--curated-only`.

**Files:**
- `Dashboard/ExportDashboard.cs` — inner loop, `TargetMain`
- `Commands/Handlers/ExportReferenceCommand.cs`

## Export › zpi

Exports the frozen binary ZPI image (`.zpi.br`) to `ZipPostLookup/Data/{cc}/`. Options: country, `--curated-only`, `--uncompressed` (writes raw `.zpi`).

**Files:**
- `Dashboard/ExportDashboard.cs` — inner loop, `TargetZpi`
- `Commands/Handlers/ExportReferenceCommand.cs`
- `Export/ZPimage/` — ZPI writer

---

# ZipPostLookup › Analyse

Analyses curated reference data and writes a Markdown report to `DataAnalysis/`. Prompts for country (or all three) and optional output path override.

**Files:**
- `Dashboard/AnalyseDashboard.cs` — `RunAsync`
- `Commands/Handlers/AnalyseCommand.cs` — `RunAsync(string[] args)`

---

# ZipPostLookup › Integrity

Verifies embedded library data against the reference DB by sampling random codes. Prompts for country (or all) and sample size (default 1000). Running all three countries writes per-country report files then enters the report pager (← → to browse, Esc to exit).

**Files:**
- `Dashboard/IntegrityDashboard.cs` — `RunAsync`, `RunAllAndBrowseAsync`, `BrowseReports`
- `Commands/Handlers/IntegrityCheckCommand.cs` — `RunAsync(string[] args)`

## Integrity › {CC}

Single-country integrity run. Streams results to screen; "Press any key" to return.

**Files:**
- `Dashboard/IntegrityDashboard.cs` — single-country branch in `RunAsync`
- `Commands/Handlers/IntegrityCheckCommand.cs`

## Integrity › All

Runs US → CA → MX in sequence with a 2-second pause between countries. Writes report files to `DataAnalysis/{cc}-integrity-{yyyyMMdd}.md`. After all three complete, enters the multi-page report browser (← → navigate countries, Esc exit).

**Files:**
- `Dashboard/IntegrityDashboard.cs` — `RunAllAndBrowseAsync`, `BrowseReports`
- Report files: `DataAnalysis/{cc}-integrity-{yyyyMMdd}.md`

---

# ZipPostLookup › Convert

Converts a GeoNames or OpenStreetMap TSV to a candidate CSV. Prompts for input TSV path (blank = back), optional country override, optional output path, and `--no-prompts` flag.

**Files:**
- `Dashboard/ConvertDashboard.cs` — `RunAsync`
- `Commands/Handlers/ConvertKnownFormatsCommand.cs` — `RunAsync(string[] args)`

---

# ZipPostLookup › Snapshot

Full export pipeline for all three countries in sequence. Step 1: `export --target ref --all --curated-only` (backs up reference CSVs to CountryDataTools). Step 2: `export --all --curated-only` (exports optimised CSVs + ZPI images to ZipPostLookup). Stops on first failure. Requires "Run Snapshot" confirmation before executing.

**Files:**
- `Dashboard/SnapshotDashboard.cs` — `RunAsync`
- `Commands/Handlers/SnapshotCommand.cs` — `RunAsync(["--yes"])`

---

# ZipPostLookup › DB

Submenu for managing the working database connection and pipeline state. Destructive subcommands (clear, reset) are highlighted red in the menu.

**Files:** `Dashboard/DbDashboard.cs` — `RunAsync`

## DB › status

Shows workdb.json config (country, provider, active run ID) and the 10 most recent pipeline runs for the configured country. Active run is marked with `▶`.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunSimpleAsync("status", [])`
- `Commands/Handlers/WorkDbCommand.cs` — `RunStatusAsync`
- `Database/Repositories/SqlRunRepository.cs` — `GetRunsAsync`
- `Database/Repositories/IRepositories.cs` — `RunSummary` record (`RunId`, `CountryId`, `SourceFilename`, `DateTimeOffset StartedAt`, `DateTimeOffset? CompletedAt`, `Status`, `Notes`)
- `Database/Sql/CommonQueries.cs` — `GetAllRuns`

## DB › test

Tests the DB connection and schema only. No output on success beyond exit code 0.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunSimpleAsync("test", [])`
- `Commands/Handlers/WorkDbCommand.cs` — `RunTestAsync`

## DB › init

Creates `workdb.json` in the current directory. Prompts for country, connection string, and provider (default: sqlserver). Tip shown: add workdb.json to .gitignore.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunInitAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunInitAsync`
- `Database/WorkDb/WorkDbConfig.cs` — `Save(path)`

## DB › newrun

Creates a new `pipeline.Runs` row and writes its ID back to `workdb.json` as `activeRunId`. Prompts for source file path (the candidate CSV for this run).

**Files:**
- `Dashboard/DbDashboard.cs` — `RunNewRunAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunNewRunAsync`
- `Database/Repositories/SqlRunRepository.cs` — `CreateRunAsync`

## DB › normalize-tz

Normalises timezone aliases (e.g. deprecated IANA names) and resolves timezones from coordinates for rows in `data.Reference` where the timezone is missing or aliased.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunSimpleAsync("normalize-tz", [])`
- `Commands/Handlers/WorkDbCommand.cs` — `RunNormalizeTzAsync`

## DB › normalize-names

Detects place-name abbreviation alternates (e.g. "St." vs "Saint") and links them via `data.Reference.AltNameOf`. Also propagates curation flags to orphaned alt-name rows whose canonical code is already curated.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunNormalizeNamesAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunNormalizeNamesAsync`
- `Validation/NormalizePlaceNamesCheck.cs`
- `Database/Sql/CommonQueries.cs` — `FixOrphanAltNames`

## DB › clear ⚠

Clears pipeline working data (`codes.Candidate`, `codes.Discrepancies`, `pipeline.Decisions`) for the selected country. `data.Reference` is **not** affected. Prompts for country.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunWithCountryAsync("clear", needsConfirm: false)`
- `Commands/Handlers/WorkDbCommand.cs` — `RunClearAsync`

## DB › reset ⚠⚠

Full wipe — removes `data.Reference` too. Requires typing the country code to confirm. Cannot be undone.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunWithCountryAsync("reset", needsConfirm: false)`
- `Commands/Handlers/WorkDbCommand.cs` — `RunResetAsync`

---

# ZipPostLookup › ZpCode Editor

Entry screen for the curation workflow. Loads all-country stats from the DB and displays them in a progress table (rows: CA / MX / US · columns: Tz. Checked / Name Checked / Curated / Total / Remaining / Progress). Below the table, a country picker lets you enter a country's uncurated code list.

**Keyboard:** ↑↓ move · Enter select country · Esc back to Dashboard

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `RunAsync`
- `Dashboard/DashboardStats.cs` — `TryLoadAllAsync`, `BuildTable`
- `Database/Sql/CommonQueries.cs` — `GetAllCountryCurationStats`

## ZpCode Editor › {CC}

Paginated browse of uncurated codes for the selected country. Left panel: code list (`Code · Admin · TZ ✓ · Nm ✓ · Name`), 15 rows per page with a count + page caption. Right panel: slim vertical stats for this country only (`TZ ✓ / Nm ✓ / Curated / Total / Remaining / Progress / Orphans` if any). If all codes are curated but orphaned alt-name rows exist, shows an orphan-fix prompt instead of the list.

**Keyboard:** ↑↓ move · PgUp/PgDn page · Enter open code · O fix orphans (when shown) · Esc back

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `BrowseCodesAsync`, `RenderListPage`, `BuildCodeListTable`, `BuildCurrentCountryStatsTable`, `LoadPageAsync`, `RunOrphanFixAsync`
- `Database/Sql/CommonQueries.cs` — `GetCurationStats`, `GetUncuratedCodeCount`, `GetUncuratedCodesPage`, `GetOrphanAltNameCount`, `FixOrphanAltNames`
- `Database/WorkDb/IWorkDbConnectionFactory.cs` — connection factory (single connection, sequential awaits — no MARS)

**DTOs (private to `ZpCodeEditorDashboard`):**
- `CodeRow` — list row: `ZpCode`, `PlaceName`, `Timezone`, `Admin1`, `Admin1Code`, `TimezoneChecked`, `NameChecked`, `NameCount`
- `CurationStats` — right-panel stats: `TotalTimezoneChecked`, `TotalNameChecked`, `TotalCurated`, `Total`, `Remaining`, `PctComplete`, `OrphanAltNames`

## ZpCode Editor › {CC} › {ZpCode}

Detail view for a single postal code. Shows all `data.Reference` rows for this code — default row first, then alternates. Each row shows: IsDefault tag, TZ ✓/✗, Nm ✓/✗, PlaceName (truncated to 33 chars), Timezone. `AltNameOf` rows show their canonical code instead of curation icons. Curation actions apply to **all rows** for this ZpCode. After each action the rows reload; if all rows are curated the view closes automatically and returns to the browse list.

**Keyboard:** C curate all · T mark TZ checked · N mark names checked · E edit timezone · Esc back

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `EditCodeAsync`, `LoadDetailRowsAsync`, `RunUpdateAsync`
- `Database/Sql/CommonQueries.cs` — `GetReferenceRowsByCode`, `MarkCodeAsCurated`, `MarkCodeTimezoneChecked`, `MarkCodeNameChecked`, `UpdateCodeTimezone`

**DTO (private):**
- `CodeDetail` — `ZpCode`, `PlaceName`, `Timezone`, `IsDefault`, `Admin1`, `Admin1Code`, `TimezoneChecked`, `NameChecked`, `AltNameOf`

## ZpCode Editor › {CC} › {ZpCode} › Edit Timezone

Simple IANA timezone text prompt. Pre-filled with the current default row's timezone value. Blank input cancels without writing. On confirm, updates Timezone + sets TimezoneChecked = 1 for all rows of this ZpCode.

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `PromptTimezoneAsync` (prompt only), `EditCodeAsync` (calls `UpdateCodeTimezone` on return)
- `Database/Sql/CommonQueries.cs` — `UpdateCodeTimezone`
