# CDT Console — Screen Reference

Reference for the CountryDataTools interactive dashboard (`countrydatatools dashboard`).
Organised by the breadcrumb path shown in the title bar so that naming any path (e.g. `ZpCode Editor › MX › 06600`) unambiguously identifies the screen, its files, and its queries.

Every screen renders its header via `HeaderBar.Render(pageTitle)`, which prints the fixed title bar
(`ZipPostLookup CDT v0.1.0`) + `  ›  {pageTitle}` + a rule + the workdb status line. The breadcrumbs
below are the `pageTitle` strings passed to `HeaderBar.Render` — they are what actually appears on screen.

**Not committed to git.** Last reconciled with source: 2026-06-10.

---

## Quick-reference index

| Breadcrumb (`pageTitle`) | Primary file | Method |
|---|---|---|
| `Dashboard` | `Dashboard/DashboardCommand.cs` | `RunAsync` → `CdtNestedMenu.RunAsync` |
| **Data Operations group** | | |
| `Enrich` | `Dashboard/EnrichDashboard.cs` | `RunAsync` |
| `Normalize` | `Dashboard/NormalizeDashboard.cs` | `RunAsync` |
| `ZpCode Editor` | `Dashboard/ZpCodeEditorDashboard.cs` | `RunAsync` |
| `Integrity › CDT DB` | `Dashboard/DbIntegrityDashboard.cs` | `RunAsync` |
| **Importing Data group** | | |
| `Ingest` | `Dashboard/IngestDashboard.cs` | `RunAsync` |
| `Convert` | `Dashboard/ConvertDashboard.cs` | `RunAsync` |
| `Validate` | `Dashboard/ValidateDashboard.cs` | `RunAsync` |
| `Coord Data` | `Dashboard/CoordDataDashboard.cs` | `RunAsync` |
| `Import Pipeline` / `Fix and Extract` | — | future/grey, no-op |
| **Exporting Data group** | | |
| `Snapshot` | `Dashboard/SnapshotDashboard.cs` | `RunAsync` |
| `Export` | `Dashboard/ExportDashboard.cs` | `RunAsync` |
| `Integrity › ZPL Data` | `Dashboard/IntegrityDashboard.cs` | `RunAsync` |
| `Export Pipeline` | — | future/grey, no-op |
| **DB Maintenance group** (direct entry points) | | |
| `DB › status` | `Dashboard/DbDashboard.cs` | `RunStatusAsync` |
| `DB › test` | `Dashboard/DbDashboard.cs` | `RunTestAsync` |
| `DB › init` | `Dashboard/DbDashboard.cs` | `RunInitAsync` |
| `DB › newrun` | `Dashboard/DbDashboard.cs` | `RunNewRunAsync` |
| `DB › clear` | `Dashboard/DbDashboard.cs` | `RunClearAsync` |
| `DB › reset` | `Dashboard/DbDashboard.cs` | `RunResetAsync` |
| **Misc group** | | |
| `Analyse` | `Dashboard/AnalyseDashboard.cs` | `RunAsync` |
| **Sub-screens** | | |
| `Ingest › ref` | `Dashboard/IngestDashboard.cs` | `RunRefAsync` |
| `Ingest › candidate` | `Dashboard/IngestDashboard.cs` | `RunCandidateAsync` |
| `Coord Data › coords` | `Dashboard/CoordDataDashboard.cs` | `RunAsync` (inner) |
| `Enrich › candidates` | `Dashboard/EnrichDashboard.cs` | inner loop |
| `Enrich › direct` | `Dashboard/EnrichDashboard.cs` | inner loop |
| `Enrich › ref` | `Dashboard/EnrichDashboard.cs` | shows help only |
| `Export › ref` | `Dashboard/ExportDashboard.cs` | inner loop |
| `Export › main` | `Dashboard/ExportDashboard.cs` | inner loop |
| `Export › zpi` | `Dashboard/ExportDashboard.cs` | inner loop |
| `Normalize › normalize-tz` | `Dashboard/NormalizeDashboard.cs` | `RunNormalizeTzAsync` |
| `Normalize › normalize-names` | `Dashboard/NormalizeDashboard.cs` | `RunNormalizeNamesAsync` |
| `Normalize › normalize-admins` | `Dashboard/NormalizeDashboard.cs` | `RunNormalizeAdminsAsync` |
| `Integrity › ZPL Data › {CC}` / `All` | `Dashboard/IntegrityDashboard.cs` | `RunZplModeAsync` / `RunZplAllAndBrowseAsync` |
| `Integrity › CDT DB › {CC}` / `All` | `Dashboard/DbIntegrityDashboard.cs` | `RunAsync` (inner) / `BrowseReports` |
| `ZpCode Editor › {CC}` (uncurated) | `Dashboard/ZpCodeEditorDashboard.cs` | `BrowseCodesAsync` |
| `ZpCode Editor › {CC} › Flagged` | `Dashboard/ZpCodeEditorDashboard.cs` | `BrowseCodesAsync(flaggedMode: true)` |
| `ZpCode Editor › {CC} › {ZpCode}` | `Dashboard/ZpCodeEditorDashboard.cs` | `ViewCodeAsync` |
| `ZpCode Editor › {CC} › {ZpCode} › Edit` | `Dashboard/ZpCodeEditorDashboard.cs` | `EditRowAsync` |
| `ZpCode Editor › {CC} › {ZpCode} › Edit › {Field}` | `Dashboard/ZpCodeEditorDashboard.cs` | `EditFieldAsync` |
| `ZpCode Editor › {CC} › Candidate` | `Dashboard/ZpCodeEditorDashboard.cs` | `BrowseCandidatesAsync` |
| `ZpCode Editor › {CC} › Candidate › {status}` | `Dashboard/ZpCodeEditorDashboard.cs` | `BrowseCandidateStatusAsync` |
| `ZpCode Editor › {CC} › Candidate › {ZpCode}` | `Dashboard/ZpCodeEditorDashboard.cs` | `ViewCandidateCodeAsync` |

---

## Shared infrastructure (every screen)

Every screen uses these regardless of breadcrumb depth. (The old `DashboardRenderer`, `MenuPrompt`,
and `CommandEntry` types were removed in the 2026-06-08/09 console consolidation — see below for replacements.)

### `Layout/` — fixed-region render helpers

| Class | File | Role |
|---|---|---|
| `HeaderBar` | `Dashboard/Layout/HeaderBar.cs` | `Render(pageTitle)` — clears screen, draws title bar + breadcrumb + rule + workdb status line. **Replaces the old `DashboardRenderer.RenderHeader`.** |
| `TitleBar` | `Dashboard/Layout/TitleBar.cs` | `Markup` — `" [bold cyan]ZipPostLookup CDT v0.1.0 [/]"` app-title fragment |
| `BreadCrumbBar` | `Dashboard/Layout/BreadCrumbBar.cs` | `Markup(path)` — `  ›  {path}` breadcrumb fragment |
| `FooterBar` | `Dashboard/Layout/FooterBar.cs` | `PressAnyKey()` — the common "Press any key to return…" line (partial adoption; some screens still inline the markup) |
| `ContentArea` | `Dashboard/Layout/ContentArea.cs` | Empty Stage-2 marker (no behaviour) |

### `Widgets/` — interactive + display widgets

| Class | File | Role |
|---|---|---|
| `CdtSelectMenu` | `Dashboard/Widgets/CdtSelectMenu.cs` | `Show<T>(choices, converter, escapeReturns, title?)` — ↑↓/Enter/Esc vertical menu. **Replaces the old `MenuPrompt`.** Used by every screen. |
| `CdtNestedMenu` | `Dashboard/Widgets/CdtNestedMenu.cs` | Accordion group menu for the root Dashboard. Defines the `MenuGroup` / `MenuItem` records (**replacing the old `CommandEntry`**). |
| `CdtCommandMenu` | `Dashboard/Widgets/CdtCommandMenu.cs` | `Render(markupHints)` — bottom key-hint bar |
| `CdtProgressBarCell` | `Dashboard/Widgets/CdtProgressBarCell.cs` | `Render(pct, width, color)` — progress-bar cell used by `DashboardStats` |
| `CdtTable` | `Dashboard/Widgets/CdtTable.cs` | Table factory (`Square()`/`Borderless()`/`Col()`…). **Currently unused** — most screens build `new Table()` inline. |

### Stats + DB context

| Class | File | Role |
|---|---|---|
| `DashboardStats` | `Dashboard/DashboardStats.cs` | `TryLoadAllAsync()` + `BuildTable()` — all-country curation stats panel with progress bars |
| `WorkDbContext` | `Database/WorkDb/WorkDbContext.cs` | `LoadAsync(dir)` — walks up tree for workdb.json, tests connection, exposes repositories + `GetFactory()` |
| `WorkDbConfig` | `Database/WorkDb/WorkDbConfig.cs` | `FindConfigFile()` / `Load()` — workdb.json DTO |
| `CommonQueries` | `Database/Sql/CommonQueries.cs` | All SQL query strings |

**Status line:** `HeaderBar` reads `workdb.json` (file read only, no DB connection) and shows
`CC: {cc}  ·  Run: {runId}` as a dim line after the rule on every screen. Silent if no workdb.json found.

---

# Dashboard (root)

The opening screen. A **5-group accordion menu** (`CdtNestedMenu`) on the left, alongside the all-country
curation stats panel (`DashboardStats.BuildTable`) on the right. Only one group is expanded at a time;
future/grey items are non-interactive. Stats refresh after each sub-command returns.

**Keyboard:** ↑↓ move (within the active level) · Enter expand group / select item · Esc collapse group, then exit to terminal

**Groups & items:**

| Group | Items (→ target) |
|---|---|
| **Data Operations** | Enrichment → `EnrichDashboard` · Normalize → `NormalizeDashboard` · Editor → `ZpCodeEditorDashboard` · Db Integrity → `DbIntegrityDashboard` |
| **Importing Data** | Import Pipeline *(future)* · Ingest → `IngestDashboard` · Convert → `ConvertDashboard` · Validate Candidate → `ValidateDashboard` · Fix and Extract *(future)* · Coord Data → `CoordDataDashboard` |
| **Exporting Data** | Export Pipeline *(future)* · Snapshot → `SnapshotDashboard` · Export → `ExportDashboard` · ZPL Integrity → `IntegrityDashboard` |
| **DB Maintenance** | Status · Test · Init · New Run · Clear · Reset *(each calls a `DbDashboard` internal entry point directly)* |
| **Misc** | Analyse → `AnalyseDashboard` |

**Files:**
- `Dashboard/DashboardCommand.cs` — `RunAsync`, `BuildGroups`
- `Dashboard/Widgets/CdtNestedMenu.cs` — `RunAsync`, `Render`, `BuildMenuTable`; `MenuGroup` / `MenuItem` records
- `Dashboard/DashboardStats.cs` — `TryLoadAllAsync`, `BuildTable`
- `Database/Sql/CommonQueries.cs` — `GetAllCountryCurationStats`

> Note: `DbDashboard.RunAsync` (its own ↑↓ submenu of all six DB ops) still exists but is **no longer
> reached from the root menu** — the DB Maintenance group invokes the six `internal` entry points
> (`RunStatusAsync`/`RunTestAsync`/`RunInitAsync`/`RunNewRunAsync`/`RunClearAsync`/`RunResetAsync`) directly.

---

# Ingest

Submenu for loading data into the working database. **Two modes** (`coords` moved to its own Coord Data screen).

**Files:** `Dashboard/IngestDashboard.cs` — `RunAsync`

## Ingest › ref

Seeds `data.Reference` and `data.CountryInfo` from the embedded library CSV for one country or all three.
Prompts for country, `--force` (re-import even if rows exist), `--info-only` (country_info only).

**Files:**
- `Dashboard/IngestDashboard.cs` — `RunRefAsync`
- `Commands/Handlers/ImportReferenceDataCommand.cs` — `RunAsync`
- `Database/Repositories/SqlReferenceRepository.cs` — `LoadFromEmbeddedCsvAsync`

## Ingest › candidate

Imports a candidate CSV against reference data for a specific country. Prompts for file path and country.

**Files:**
- `Dashboard/IngestDashboard.cs` — `RunCandidateAsync`
- `Commands/Handlers/ImportCandidatesCommand.cs` — `RunAsync`
- `Database/Repositories/SqlCandidateRepository.cs` — `InsertBatchAsync`

---

# Coord Data

Bulk-resolves timezones for rows in `data.Reference` that have coordinates but no timezone.
Prompts for source CSV path, optional country filter, batch size, and dry-run flag. (Extracted from the
old `Ingest › coords` screen.)

**Files:**
- `Dashboard/CoordDataDashboard.cs` — `RunAsync`
- `Commands/IngestCommand.cs` → `Commands/Handlers/EnrichReferenceFromCoordinatesCommand.cs` (via `ingest coords`)

---

# Validate

Validates a candidate CSV and guides through fix/extract steps. Prompts for file path (blank = back) then country.
Option: `--no-prompts` applies fix + extract automatically.

**Files:**
- `Dashboard/ValidateDashboard.cs` — `RunAsync`
- `Commands/Handlers/ValidateCommand.cs` — `RunAsync`

---

# Convert

Converts a GeoNames or OpenStreetMap TSV to a candidate CSV. Prompts for input TSV path (blank = back),
optional country override, optional output path, and `--no-prompts`. Leading/trailing quotes are stripped from paths.

**Files:**
- `Dashboard/ConvertDashboard.cs` — `RunAsync`, `StripPathQuotes`
- `Commands/Handlers/ConvertKnownFormatsCommand.cs` — `RunAsync`

---

# Enrich

Submenu for enriching reference data via the API pool. Three modes.

**Files:** `Dashboard/EnrichDashboard.cs` — `RunAsync`

## Enrich › candidates

Resolves discrepancies in a pipeline run via the enrichment API pool. Prompts for country, limit, dry-run.

**Files:**
- `Dashboard/EnrichDashboard.cs` — inner loop
- `Commands/Handlers/EnrichCandidatesCommand.cs` — `RunAsync`
- `Enrichment/Api/` — the API pool + `EnrichmentApiFactory`

## Enrich › direct

Backfills uncurated `data.Reference` rows directly (not pipeline-bound, no run ID required). Same prompts.

**Files:**
- `Dashboard/EnrichDashboard.cs` — inner loop
- `Commands/Handlers/EnrichDirectCommand.cs` — `RunAsync`

## Enrich › ref

Complex argument shape; shows CLI help text only. Use `countrydatatools enrich ref` directly.

**Files:**
- `Dashboard/EnrichDashboard.cs` — shows help
- `Commands/EnrichCommand.cs` — called with `["-h"]`

---

# Normalize

Submenu for normalisation passes. Four items — **Normalize-All** is future/grey; the other three each
prompt for a country and run via `db <subcommand>`. (Extracted from the old `DB › normalize-*` screens.)

**Files:** `Dashboard/NormalizeDashboard.cs` — `RunAsync`, `RunAndPauseAsync`

## Normalize › normalize-tz

Four steps in sequence: (1, US only) prompt-to-purge ZIPs outside 00501–99950; (2) reset false
`TimezoneChecked=1` marks on blank-timezone rows; (3) normalise deprecated IANA aliases + de-dupe;
(4) resolve IANA timezones from coordinates where `TimezoneChecked=0`. Prompts: US / CA / MX / All.

**Files:**
- `Dashboard/NormalizeDashboard.cs` — `RunNormalizeTzAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunNormalizeTzAsync`
- `Validation/Us/UsCountryRules.cs` — `IsOutOfBoundsUs`
- `Database/Sql/CommonQueries.cs` — `CountOutOfRangeUs`, `PurgeOutOfRangeUsAdmins`, `PurgeOutOfRangeUs`

## Normalize › normalize-names

Three steps per country: (1) link place-name abbreviation alternates via `AltNameOf`; (2) demote duplicate
`IsDefault=1` rows (`FixDuplicateIsDefaults`); (3) propagate curation flags to orphan alt-name children
(`FixOrphanAltNames`). Prompts: US / CA / MX / All.

**Files:**
- `Dashboard/NormalizeDashboard.cs` — `RunNormalizeNamesAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunNormalizeNamesAsync`
- `Validation/Export/NormalizePlaceNamesCheck.cs`
- `Database/Sql/CommonQueries.cs` — `FixDuplicateIsDefaults`, `FixOrphanAltNames`

## Normalize › normalize-admins

Backfills missing `Admin1Code` / `Admin1Name` using ZIP-prefix rules (`ResolveAdmin1`). **US and MX only**
(CA has no prefix rule). Prompts: US / MX / All (US + MX).

**Files:**
- `Dashboard/NormalizeDashboard.cs` — `RunNormalizeAdminsAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunNormalizeAdminsAsync`
- `Validation/CountryRulesFactory.cs` — `ICountryRules.ResolveAdmin1`

---

# Export

Submenu for exporting reference data. Three targets.

**Files:** `Dashboard/ExportDashboard.cs` — `RunAsync`

## Export › ref

Exports the source-of-truth reference CSV to `CountryDataTools/Data/{cc}/`, then auto-updates `{cc}_info.json`
and `countries.json` (CodeCount, Curated, CurationStatus, per-division counts). Options: country, `--curated-only`.

**Files:**
- `Dashboard/ExportDashboard.cs` — inner loop (`TargetRef`)
- `Commands/Handlers/ExportReferenceCommand.cs` — `RunRefExportAsync`, `UpdateCountryMetadataAsync`
- `Database/Sql/CommonQueries.cs` — `GetAdminDivisionCounts`

## Export › main

Exports optimised library CSV (range-compressed, AltNameOf + Flagged rows excluded) to `ZipPostLookup/Data/{cc}/`.
Options: country, `--curated-only`.

**Files:**
- `Dashboard/ExportDashboard.cs` — inner loop (`TargetMain`)
- `Commands/Handlers/ExportReferenceCommand.cs` — `RunMainExportAsync`
- `Export/ExportPipeline.cs` + `Export/Stages/`

## Export › zpi

Exports the frozen binary ZPI image (`.zpi.br`) to `ZipPostLookup/Data/{cc}/`. Options: country,
`--curated-only`, `--uncompressed`.

**Files:**
- `Dashboard/ExportDashboard.cs` — inner loop (`TargetZpi`)
- `Commands/Handlers/ExportReferenceCommand.cs`
- `Export/ZPimage/` — ZPI writer

---

# Snapshot

Full export pipeline for all three countries in sequence. Step 1: `export --target ref --all --curated-only`.
Step 2: `export --all --curated-only` (optimised CSVs + ZPI images). Stops on first failure. Requires confirmation.

**Files:**
- `Dashboard/SnapshotDashboard.cs` — `RunAsync`
- `Commands/Handlers/SnapshotCommand.cs` — `RunAsync(["--yes"])`

---

# Analyse

Analyses curated reference data and writes a Markdown report to `DataAnalysis/`. Prompts for country (or all) + optional output path.

**Files:**
- `Dashboard/AnalyseDashboard.cs` — `RunAsync`
- `Commands/Handlers/AnalyseCommand.cs` — `RunAsync` (+ `Commands/Handlers/Analyse/` passes)

---

# Integrity › ZPL Data

**ZPL-only** integrity screen (the old "ZPL Data vs CDT DB" mode picker was removed; CDT DB scanning now
lives in the separate **Db Integrity** screen — see below). Entry goes straight to the country picker
(US / CA / MX / All). Samples random curated codes from the DB and verifies they resolve correctly against
the embedded library CSV/ZPI. Prompts for test count (default 1000). Running All enters a ← → report browser.

**Keyboard:** ↑↓/Enter menu · ← → browse reports (All mode) · Esc back

**Files:**
- `Dashboard/IntegrityDashboard.cs` — `RunAsync`, `RunZplModeAsync`, `RunZplAllAndBrowseAsync`, `BrowseReports`
- `Commands/Handlers/IntegrityCheckCommand.cs` — `RunForCountryAsync`
- `Commands/Display/IntegrityDisplay.cs` — `PrintSummary`
- Report files: `DataAnalysis/{cc}-integrity-{yyyyMMdd}.md`

---

# Integrity › CDT DB

**Db Integrity** screen (Data Operations group). Scans `data.Reference` for 11 data-quality problems
(admin mismatches, missing admin1, orphan AltNameOf, duplicate IsDefault, invalid ZpCode format, curated
codes with no default row, alt-name rows marked IsDefault, curated blank place names, TimezoneChecked rows
with blank timezone, **gold-code regressions**). Flagged rows excluded from all checks. Country picker
(US / CA / MX / All); All enters a ← → report browser. Writes `DataAnalysis/{cc}-db-integrity-{yyyyMMdd}.md`.

**Keyboard:** ↑↓/Enter menu · ← → browse reports (All mode) · Esc back

**Files:**
- `Dashboard/DbIntegrityDashboard.cs` — `RunAsync`, `BrowseReports`
- `Commands/Handlers/CdtDbIntegrityCommand.cs` — `RunForCountryAsync`, `PrintSummaryTable`, `BuildMarkdownReport`
- `Database/Sql/CommonQueries.cs` — `GetCuratedDefaultAdminCodes`, `GetCuratedMissingAdmin1Count`, `GetOrphanAltNamesDetailed`, `GetDuplicateIsDefaultCodes`, `GetInvalidZpCodes`, `GetCuratedCodesWithNoDefault`, `GetAltNameRowsMarkedDefault`, `GetCuratedBlankPlaceNames`, `GetCheckedBlankTimezones`, `GetGoldCodesFailingConditions`

---

# DB Maintenance

The DB Maintenance group invokes six `DbDashboard` entry points directly (no intermediate submenu).
Destructive ops (clear, reset) are styled red.

**Files:** `Dashboard/DbDashboard.cs`

## DB › status

Shows workdb.json config (country, provider, active run ID) and the 10 most recent pipeline runs. Active run marked `▶`.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunStatusAsync` → `RunSimpleAsync("status")`
- `Commands/Handlers/WorkDbCommand.cs` — `RunStatusAsync`
- `Database/Repositories/SqlRunRepository.cs` — `GetRunsAsync`; `RunSummary` record (`RunId`, `CountryId`, `SourceFilename`, `DateTimeOffset StartedAt`, `DateTimeOffset? CompletedAt`, `Status`, `Notes`)
- `Database/Sql/CommonQueries.cs` — `GetAllRuns`

## DB › test

Tests the DB connection and schema only. No output on success beyond exit code 0.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunTestAsync` → `RunSimpleAsync("test")`
- `Commands/Handlers/WorkDbCommand.cs` — `RunTestAsync`

## DB › init

Creates `workdb.json` in the current directory. Prompts for country, connection string, provider (default sqlserver).

**Files:**
- `Dashboard/DbDashboard.cs` — `RunInitAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunInitAsync`
- `Database/WorkDb/WorkDbConfig.cs` — `Save(path)`

## DB › newrun

Creates a new `pipeline.Runs` row and writes its ID to `workdb.json` as `activeRunId`. Prompts for source file path.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunNewRunAsync`
- `Commands/Handlers/WorkDbCommand.cs` — `RunNewRunAsync`
- `Database/Repositories/SqlRunRepository.cs` — `CreateRunAsync`

## DB › clear ⚠

Clears pipeline working data (`codes.Candidate`, `codes.Discrepancies`, `pipeline.Decisions`) for the selected
country. `data.Reference` is **not** affected. Prompts for country.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunClearAsync` → `RunWithCountryAsync("clear")`
- `Commands/Handlers/WorkDbCommand.cs` — `RunClearAsync`

## DB › reset ⚠⚠

Full wipe — removes `data.Reference` too. Handler requires typing the country code to confirm. Cannot be undone.

**Files:**
- `Dashboard/DbDashboard.cs` — `RunResetAsync` → `RunWithCountryAsync("reset")`
- `Commands/Handlers/WorkDbCommand.cs` — `RunResetAsync`

---

# ZpCode Editor

Entry screen. Loads all-country stats and displays them in a progress table, then a country picker
(US / CA / MX / ← Back), then a **mode picker**: `Edit Uncurated` / `Edit Flagged` / `Candidate` / ← Back.

**Keyboard:** ↑↓ move · Enter select · Esc back to Dashboard

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `RunAsync`
- `Dashboard/DashboardStats.cs` — `TryLoadAllAsync`, `BuildTable`
- `Database/Sql/CommonQueries.cs` — `GetAllCountryCurationStats`

## ZpCode Editor › {CC}  (Edit Uncurated)

Paginated browse of individual uncurated `data.Reference` rows (15/page). Left: row list (Code · D · TZ✓ · Nm✓ · ⚑ ·
Place Name · Timezone · Lat · Lng · AltNameOf · Admin). Right: slim per-country stats (+ Gold ★ / Orphans when present).
If all curated but orphan alt-name rows exist, shows the `O` orphan-fix prompt.

**Keyboard:** ↑↓ move · PgUp/PgDn page · Enter open detail · O fix orphans (when shown) · Esc back

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `BrowseCodesAsync`, `RenderBrowsePage`, `BuildBrowseTable`, `BuildCurrentCountryStatsTable`, `LoadBrowsePageAsync`, `RunOrphanFixAsync`
- `Database/Sql/CommonQueries.cs` — `GetCurationStats`, `GetBrowseRowCount`, `GetBrowseRowsPage`, `GetOrphanAltNameCount`, `FixOrphanAltNames`, `GetGoldCodeCount`

**DTOs (private):** `BrowseRow`, `CurationStats`

## ZpCode Editor › {CC} › Flagged

Same browse layout filtered to `Flagged = 1` rows. Orphan-fix prompt suppressed. "No flagged codes" when empty.

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `BrowseCodesAsync(country, flaggedMode: true)`
- `Database/Sql/CommonQueries.cs` — `GetFlaggedBrowseRowCount`, `GetFlaggedBrowseRowsPage`

## ZpCode Editor › {CC} › {ZpCode}

Detail view: all `data.Reference` rows for the code (default first). Columns D · TZ✓ · Nm✓ · ⚑ · ★ (Gold) ·
PlaceName · Timezone · Lat · Lng · AltNameOf · Admin1. Actions C/T/N/F apply to **all rows**. C/T/N auto-run
gold certification when the code becomes fully curated. If all rows become curated the view closes.

**Keyboard:** ↑↓ move · C curate all · T TZ checked · N names checked · E edit selected row · F flag/unflag all · Esc back

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `ViewCodeAsync`, `LoadDetailRowsAsync`, `RunUpdateAsync`, `BuildDetailTable`, `TryCertifyGoldAsync`
- `Database/Sql/CommonQueries.cs` — `GetCodeDetailRows`, `MarkCodeAsCurated`, `MarkCodeTimezoneChecked`, `MarkCodeNameChecked`, `SetReferenceFlagReasonById` (per-row v/f/c/o flag reason), `CheckGoldEligibility`, `SetGoldCode`

**DTO (private):** `DetailRow` (incl. `IsGold`)

## ZpCode Editor › {CC} › {ZpCode} › Edit

Vertical field editor for a single row. 10 editable fields. Enter to edit highlighted field; Esc back.
A renamed Code is propagated back to `ViewCodeAsync`.

**Editable fields:** Code · PlaceName · Timezone · Lat · Lng · AltNameOf · Admin1Code · Admin1Name · Admin2Code · Admin2Name

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `EditRowAsync`, `BuildEditTable`, `GetFieldValue`

## ZpCode Editor › {CC} › {ZpCode} › Edit › {Field}

Single-field prompt. Shows current value; blank = cancel. Field-specific behaviour:

| Field | Behaviour |
|---|---|
| `Code` | Y/N confirm; updates the specific `ReferenceId` (`RenameZpCode`); returns new code to `EditRowAsync` |
| `PlaceName` / `Timezone` / `Lat` / `Lng` | Update by `ReferenceId` (`Update…ById`) |
| `AltNameOf` | `---` / `—` clears to NULL (`UpdateReferenceAltNameOfById`) |
| `Admin1Code` / `Admin1Name` / `Admin2Code` / `Admin2Name` | MERGE into `data.ReferenceAdmins` (`UpsertReferenceAdmin`) |

After the write, reloads via `GetCodeDetailRowById`.

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `EditFieldAsync`, `LoadSingleDetailRowAsync`
- `Database/Sql/CommonQueries.cs` — `RenameZpCode`, `UpdateReferencePlaceNameById`, `UpdateReferenceTimezoneById`, `UpdateReferenceCoordsById` (paired Lat+Lng write — edited on a dedicated Coordinates page), `UpdateReferenceAltNameOfById`, `UpsertReferenceAdmin`, `GetCodeDetailRowById`, `GetAdminLevelIdForLevel`

## ZpCode Editor › {CC} › Candidate

Browse pipeline candidate codes. Loads `workdb.json`, queries the per-status summary, and shows a status-picker
menu (each status + its row count). No active-run requirement (RunId filtering was removed).

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `BrowseCandidatesAsync`
- `Database/Sql/CommonQueries.cs` — `GetCandidateStatusSummary`

**DTO (private):** `CandidateStatusCount`

## ZpCode Editor › {CC} › Candidate › {status}

Paginated browse of all `codes.Candidate` rows matching a status. Enter navigates into the candidate detail view.

**Keyboard:** ↑↓ move · PgUp/PgDn page · Enter view · Esc back

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `BrowseCandidateStatusAsync`, `LoadCandidatePageAsync`, `BuildCandidateBrowseTable`, `CandidateStatusMarkup`
- `Database/Sql/CommonQueries.cs` — `GetCandidatesBrowsePage`, `GetCandidatesBrowseCount`

**DTO (private):** `CandidateBrowseRow`

## ZpCode Editor › {CC} › Candidate › {ZpCode}

Detail view for one candidate ZpCode: candidate rows (top) + matching non-flagged `data.Reference` rows (bottom).
`R` rejects all candidate rows for the code; `U` un-rejects back to Pending; Esc returns. (Uses an inline query
casting `c.IsDefault AS BIT`.)

**Files:**
- `Dashboard/ZpCodeEditorDashboard.cs` — `ViewCandidateCodeAsync`
- `Database/Sql/CommonQueries.cs` — `RejectCandidateZpCode`
