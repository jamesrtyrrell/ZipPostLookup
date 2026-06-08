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
| `ZipPostLookup › Integrity › ZPL Data › {CC}` | `Dashboard/IntegrityDashboard.cs` | `RunZplModeAsync` |
| `ZipPostLookup › Integrity › ZPL Data › All` | `Dashboard/IntegrityDashboard.cs` | `RunZplAllAndBrowseAsync` |
| `ZipPostLookup › Integrity › CDT DB › {CC}` | `Dashboard/IntegrityDashboard.cs` | `RunCdtDbModeAsync` |
| `ZipPostLookup › Integrity › CDT DB › All` | `Dashboard/IntegrityDashboard.cs` | `RunCdtDbModeAsync` |
| `ZipPostLookup › Convert` | `Dashboard/ConvertDashboard.cs` | `RunAsync` |
| `ZipPostLookup › Snapshot` | `Dashboard/SnapshotDashboard.cs` | `RunAsync` |
| `ZipPostLookup › DB` | `Dashboard/DbDashboard.cs` | `RunAsync` |
| `ZipPostLookup › DB › status` | `Dashboard/DbDashboard.cs` | `RunSimpleAsync("status")` |
| `ZipPostLookup › DB › test` | `Dashboard/DbDashboard.cs` | `RunSimpleAsync("test")` |
| `ZipPostLookup › DB › init` | `Dashboard/DbDashboard.cs` | `RunInitAsync` |
| `ZipPostLookup › DB › newrun` | `Dashboard/DbDashboard.cs` | `RunNewRunAsync` |
| `ZipPostLookup › DB › normalize-tz` | `Dashboard/DbDashboard.cs` | `RunSimpleAsync("normalize-tz")` |
| `ZipPostLookup › DB › normalize-names` | `Dashboard/DbDashboard.cs` | `RunNormalizeNamesAsync` |
| `ZipPostLookup › DB › normalize-admins` | `Dashboard/DbDashboard.cs` | `RunNormalizeAdminsAsync` |
| `ZipPostLookup › DB › purge-oob-us` | `Dashboard/DbDashboard.cs` | `RunSimpleAsync("purge-oob-us")` |
| `ZipPostLookup › DB › clear` | `Dashboard/DbDashboard.cs` | `RunWithCountryAsync("clear")` |
| `ZipPostLookup › DB › reset` | `Dashboard/DbDashboard.cs` | `RunWithCountryAsync("reset")` |
| `ZipPostLookup › ZpCode Editor` | `Dashboard/ZpCodeEditorDashboard.cs` | `RunAsync` |
| `ZipPostLookup › ZpCode Editor › {CC}` | `Dashboard/ZpCodeEditorDashboard.cs` | `BrowseCodesAsync` (uncurated) |
| `ZipPostLookup › ZpCode Editor › {CC} › Flagged` | `Dashboard/ZpCodeEditorDashboard.cs` | `BrowseCodesAsync` (flagged mode) |
| `ZipPostLookup › ZpCode Editor › {CC} › {ZpCode}` | `Dashboard/ZpCodeEditorDashboard.cs` | `ViewCodeAsync` |
| `ZipPostLookup › ZpCode Editor › {CC} › {ZpCode} › Edit` | `Dashboard/ZpCodeEditorDashboard.cs` | `EditRowAsync` |
| `ZipPostLookup › ZpCode Editor › {CC} › {ZpCode} › Edit › {Field}` | `Dashboard/ZpCodeEditorDashboard.cs` | `EditFieldAsync` |

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

**Commands (in menu order):** ingest · validate · enrich · export · analyse · integrity · convert · snapshot · db · editor

**Files:**
- `Dashboard/DashboardCommand.cs` — `RunAsync`, `Render`, `BuildMenuTable`, `BuildCommands`
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

Exports optimised library CSV (range-compressed, AltNameOf rows excluded, Flagged rows excluded) to `ZipPostLookup/Data/{cc}/`. Options: country, `--curated-only`.

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

Two-mode integrity screen. First menu selects **ZPL Data** (library vs DB sampling) or **CDT DB** (DB quality scan). Second menu selects country (US / CA / MX / All). Running All enters a ← → report browser after all countries complete.

**Keyboard:** ↑↓/Enter menu · ← → browse reports (All mode) · Esc back

**Files:**
- `Dashboard/IntegrityDashboard.cs` — `RunAsync`, `RunZplModeAsync`, `RunCdtDbModeAsync`, `BrowseReports`

## Integrity › ZPL Data › {CC}

Samples random curated codes from the DB and verifies they resolve correctly against the embedded library CSV/ZPI. Prompts for sample size (default 1000). Single-country run streams results to screen; "Press any key" to return.

**Files:**
- `Dashboard/IntegrityDashboard.cs` — `RunZplModeAsync`
- `Commands/Handlers/IntegrityCheckCommand.cs` — `RunForCountryAsync`
- `Commands/Display/IntegrityDisplay.cs` — `PrintSummary`

## Integrity › ZPL Data › All

Runs US → CA → MX in sequence with a 2-second pause between countries. Writes report files to `DataAnalysis/{cc}-integrity-{yyyyMMdd}.md`. After all three complete, enters the ← → report browser showing `IntegrityCheckSummary` per country.

**Files:**
- `Dashboard/IntegrityDashboard.cs` — `RunZplAllAndBrowseAsync`, `BrowseReports`
- Report files: `DataAnalysis/{cc}-integrity-{yyyyMMdd}.md`

## Integrity › CDT DB › {CC}

Scans `data.Reference` for 9 data-quality problems (admin mismatches, missing admin1, orphan AltNameOf, duplicate IsDefault, invalid ZpCode format, curated codes with no default row, alt-name rows marked IsDefault, curated blank place names, TimezoneChecked rows with blank timezone). Flagged+Curated rows are excluded from all checks. Writes a Markdown report to `DataAnalysis/{cc}-db-integrity-{yyyyMMdd}.md`.

**Files:**
- `Dashboard/IntegrityDashboard.cs` — `RunCdtDbModeAsync`
- `Commands/Handlers/CdtDbIntegrityCommand.cs` — `RunForCountryAsync`, `PrintSummaryTable`, `BuildMarkdownReport`
- `Database/Sql/CommonQueries.cs` — `GetCuratedDefaultAdminCodes`, `GetCuratedMissingAdmin1Count`, `GetOrphanAltNamesDetailed`, `GetDuplicateIsDefaultCodes`, `GetInvalidZpCodes`, `GetCuratedCodesWithNoDefault`, `GetAltNameRowsMarkedDefault`, `GetCuratedBlankPlaceNames`, `GetCheckedBlankTimezones`

## Integrity › CDT DB › All

Runs US → CA → MX in sequence. After all three complete, enters the ← → report browser showing `DbCheckResults` summary table per country via `CdtDbIntegrityCommand.PrintSummaryTable`.

---

# ZipPostLookup › Convert

Converts a GeoNames or OpenStreetMap TSV to a candidate CSV. Prompts for input TSV path (blank = back), optional country override, optional output path, and `--no-prompts` flag. Leading/trailing `"` or `'` are stripped from path inputs automatically.

**Files:**
- `Dashboard/ConvertDashboard.cs` — `RunAsync`, `StripPathQuotes`
- `Commands/Handlers/ConvertKnownFormatsCommand.cs` — `RunAsync(string[] args)`

---

# ZipPostLookup › Snapshot

Full export pipeline for all three countries in sequence. Step 1: `export --target ref --all --curated-only` (backs up reference CSVs to CountryDataTools). Step 2: `export --all --curated-only` (exports optimised CSVs + ZPI images to ZipPostLookup). Stops on first failure. Requires "Run Snapshot" confirmation before executing.

**Files:**
- `Dashboard/SnapshotDashboard.cs` — `RunAsync`
- `Commands/Handlers/SnapshotCommand.cs` — `RunAsync(["--yes"])`

---

# ZipPostLookup › DB

Submenu for managing the working database connection and pipeline state. Destructive subcommands (purge-oob-us, clear, reset) are highlighted red in the menu.

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

Three steps run in sequence per country:
1. Detects place-name abbreviation alternates (e.g. "St." vs "Saint") and links them via `data.Reference.AltNameOf`. Clears `AltNameOf` from any `IsDefault=1` row before processing.
2. Demotes duplicate `IsDefault=1` rows — keeps the earliest non-AltNameOf row as winner, clears `IsDefault` on all others (`FixDuplicateIsDefaults`).
3. Propagates curation flags (TimezoneChecked + NameChecked) from canonical rows to any still-uncurated alt-name children (`FixOrphanAltNames`).

Prompts: country (US / CA / MX / All).

**Files:**
- `Dashboard/DbDashboard.cs` — `RunNormalizeNamesAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunNormalizeNamesAsync`
- `Validation/Export/NormalizePlaceNamesCheck.cs` — abbreviation-linking logic
- `Database/Sql/CommonQueries.cs` — `FixDuplicateIsDefaults`, `FixOrphanAltNames`

## DB › normalize-admins

Backfills missing `Admin1Code` / `Admin1Name` in `data.ReferenceAdmins` using ZIP-prefix rules (`ResolveAdmin1`). US and MX only (CA has no prefix rule). Prompts: country (US / MX / All US+MX).

**Files:**
- `Dashboard/DbDashboard.cs` — `RunNormalizeAdminsAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunNormalizeAdminsAsync`
- `Validation/CountryRulesFactory.cs` — `ICountryRules.ResolveAdmin1`

## DB › purge-oob-us ⚠

Removes US `data.Reference` rows whose ZipCode falls outside the valid range 00501–99950. One-shot with no country prompt.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunSimpleAsync("purge-oob-us", [])`
- `Commands/Handlers/WorkDbCommand.cs` — `RunPurgeOutOfRangeUsAsync`
- `Database/Sql/CommonQueries.cs` — `PurgeOutOfRangeUsAdmins`, `PurgeOutOfRangeUs`

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

Entry screen. Loads all-country stats from the DB and displays them in a progress table. Below the table, a country picker lets you choose a country, then a mode picker selects **Edit Uncurated** or **Edit Flagged**.

**Keyboard:** ↑↓ move · Enter select · Esc back to Dashboard

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `RunAsync`
- `Dashboard/DashboardStats.cs` — `TryLoadAllAsync`, `BuildTable`
- `Database/Sql/CommonQueries.cs` — `GetAllCountryCurationStats`

## ZpCode Editor › {CC}

Paginated browse of individual uncurated `data.Reference` rows for the selected country (15 rows/page). Left panel: row list with columns Code · D (IsDefault) · TZ✓ · Nm✓ · ⚑ (Flagged) · Place Name · Timezone · Lat · Lng · AltNameOf · Admin. Right panel: slim vertical stats for this country (TZ ✓ / Nm ✓ / Curated / Total / Remaining / Progress / Orphans if any). If all codes curated but orphaned alt-name rows exist, shows the orphan-fix prompt.

**Keyboard:** ↑↓ move · PgUp/PgDn page · Enter open detail · O fix orphans (when shown) · Esc back

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `BrowseCodesAsync`, `RenderBrowsePage`, `BuildBrowseTable`, `BuildCurrentCountryStatsTable`, `LoadBrowsePageAsync`, `RunOrphanFixAsync`
- `Database/Sql/CommonQueries.cs` — `GetCurationStats`, `GetBrowseRowCount`, `GetBrowseRowsPage`, `GetOrphanAltNameCount`, `FixOrphanAltNames`

**DTOs (private):**
- `BrowseRow` — `ZpCode`, `PlaceName`, `Timezone`, `IsDefault`, `Lat`, `Lng`, `AltNameOf`, `Admin1`, `Admin1Code`, `TimezoneChecked`, `NameChecked`, `Flagged`
- `CurationStats` — `TotalTimezoneChecked`, `TotalNameChecked`, `TotalCurated`, `Total`, `Remaining`, `PctComplete`, `OrphanAltNames`

## ZpCode Editor › {CC} › Flagged

Same layout as the uncurated browse screen but filtered to `Flagged = 1` rows. Header shows `ZpCode Editor › {CC} › Flagged`. The orphan-fix prompt is suppressed. "No flagged codes" shown when empty.

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `BrowseCodesAsync(country, flaggedMode: true)`
- `Database/Sql/CommonQueries.cs` — `GetFlaggedBrowseRowCount`, `GetFlaggedBrowseRowsPage`

## ZpCode Editor › {CC} › {ZpCode}

Detail view for a single postal code. Shows all `data.Reference` rows for this code in a table (default row first). Columns: D · TZ✓ · Nm✓ · ⚑ · PlaceName · Timezone · Lat · Lng · AltNameOf · Admin1. Selected row highlighted. Curation actions C/T/N/F apply to **all rows** for this ZpCode. After each action rows reload; if all rows become curated the view closes automatically.

**Keyboard:** ↑↓ move · C curate all · T TZ checked · N names checked · E edit selected row · F flag/unflag all · Esc back

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `ViewCodeAsync`, `LoadDetailRowsAsync`, `RunUpdateAsync`, `BuildDetailTable`
- `Database/Sql/CommonQueries.cs` — `GetCodeDetailRows`, `MarkCodeAsCurated`, `MarkCodeTimezoneChecked`, `MarkCodeNameChecked`, `FlagCode`, `UnflagCode`

**DTO (private):**
- `DetailRow` — `ReferenceId`, `ZpCode`, `PlaceName`, `Timezone`, `IsDefault`, `Lat`, `Lng`, `AltNameOf`, `Admin1`, `Admin1Code`, `Admin2`, `Admin2Code`, `TimezoneChecked`, `NameChecked`, `Flagged`

## ZpCode Editor › {CC} › {ZpCode} › Edit

Vertical field editor for a single `data.Reference` row. Lists all 10 editable fields; selected field is highlighted. Press Enter to edit the highlighted field; Esc returns to the detail view. If the Code field was renamed, the new code is propagated back to `ViewCodeAsync` so the detail view stays in sync.

**Editable fields:** Code · PlaceName · Timezone · Lat · Lng · AltNameOf · Admin1Code · Admin1Name · Admin2Code · Admin2Name

**Keyboard:** ↑↓ move · Enter edit field · Esc back

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `EditRowAsync`, `BuildEditTable`, `GetFieldValue`

## ZpCode Editor › {CC} › {ZpCode} › Edit › {Field}

Single-field prompt screen. Shows the current value and prompts for a new value (blank = cancel). Field-specific behaviour:

| Field | Behaviour |
|---|---|
| `Code` | Requires Y/N confirmation before writing. Updates only the specific `ReferenceId` row (`RenameZpCode`). Returns the new code to `EditRowAsync` so the header updates. |
| `PlaceName` | Updates by `ReferenceId` (`UpdateReferencePlaceNameById`). |
| `Timezone` | Updates by `ReferenceId` (`UpdateReferenceTimezoneById`). |
| `Lat` / `Lng` | Updates by `ReferenceId` (`UpdateReferenceLatById` / `UpdateReferenceLngById`). |
| `AltNameOf` | `---` or `—` input clears to NULL. Updates by `ReferenceId` (`UpdateReferenceAltNameOfById`). |
| `Admin1Code` / `Admin1Name` | MERGE into `data.ReferenceAdmins` at level 1 (`UpsertReferenceAdmin`). |
| `Admin2Code` / `Admin2Name` | MERGE into `data.ReferenceAdmins` at level 2 (`UpsertReferenceAdmin`). |

After the write, reloads the row via `GetCodeDetailRowById` so the edit table reflects the saved value.

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `EditFieldAsync`, `LoadSingleDetailRowAsync`
- `Database/Sql/CommonQueries.cs` — `RenameZpCode`, `UpdateReferencePlaceNameById`, `UpdateReferenceTimezoneById`, `UpdateReferenceLatById`, `UpdateReferenceLngById`, `UpdateReferenceAltNameOfById`, `UpsertReferenceAdmin`, `GetCodeDetailRowById`, `GetAdminLevelIdForLevel`
