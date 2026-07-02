using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

public static class DashboardCommand
{
    public static async Task<int> RunAsync(string[] _)
    {
        var stats  = await DashboardStats.TryLoadAllAsync();
        var groups = BuildGroups();
        await CdtNestedMenu.RunAsync(groups, stats);
        return 0;
    }

    private static IReadOnlyList<MenuGroup> BuildGroups() =>
    [
        new MenuGroup("System", "Country management and system configuration",
        [
            new MenuItem("Country Management", "Enable/disable countries, initialize from JSON", CountryManagementDashboard.RunAsync),
        ]),

        new MenuGroup("Data Operations", "Curating uncurated data, finding missing information",
        [
            new MenuItem("Enrichment",      "Enrich uncurated DB or candidate data via APIs",       EnrichDashboard.RunAsync),
            new MenuItem("Normalize",       "Fix timezone aliases, names, and admin data",           NormalizeDashboard.RunAsync),
            new MenuItem("Editor",          "Browse and curate uncurated reference codes",           ZpCodeEditorDashboard.RunAsync),
            new MenuItem("Find Gold",       "Certify all reference ZpCodes that are Gold Standard",  FindGoldDashboard.RunAsync),
            new MenuItem("Auto-Promote",    "Auto-promote candidate aliases (Phase 3)",              AutoPromoteDashboard.RunAsync),
            new MenuItem("Db Integrity",    "Scan DB for admin errors, orphans, duplicates",        DbIntegrityDashboard.RunAsync),
        ]),

        new MenuGroup("Importing Data", "Working with, cleaning, and preparing data to import",
        [
            new MenuItem("Import Pipeline",    "Entire process, step by step",                    null, IsFuture: true),
            new MenuItem("Auto-Import",        "AI-powered: auto-detect format and import",       AutoImportDashboard.RunAsync),
            new MenuItem("Ingest",             "Import ref data or candidate CSVs",               IngestDashboard.RunAsync),
            new MenuItem("ZpCodes Only",       "Import a flat csv of codes to check",             CodesOnlyDashboard.RunAsync),
            new MenuItem("Convert",            "Convert known formats from OSM, GeoNames, etc.",  ConvertDashboard.RunAsync),
            new MenuItem("Validate Candidate", "Check candidate files for issues",                ValidateDashboard.RunAsync),
            new MenuItem("Fix and Extract",    "Fix structural and extract problem rows",          null, IsFuture: true),
            new MenuItem("Coord Data",         "Bulk-resolve timezones from a coordinates CSV",   CoordDataDashboard.RunAsync),
        ]),

        new MenuGroup("Exporting Data", "Saving data or exporting curated data to ZPL",
        [
            new MenuItem("Export Pipeline", "Entire process, step by step",                        null, IsFuture: true),
            new MenuItem("Snapshot",        "Full export pipeline for all countries",               SnapshotDashboard.RunAsync),
            new MenuItem("Export",          "Export reference data to CSV or ZP image",            ExportDashboard.RunAsync),
            new MenuItem("ZPL Integrity",   "Compare library vs DB — detects stale exports",       IntegrityDashboard.RunAsync),
        ]),

        new MenuGroup("DB Maintenance", "Operations to test, initialise, purge, reset the database",
        [
            new MenuItem("Status",  "Show config, connection status, and recent runs",             DbDashboard.RunStatusAsync),
            new MenuItem("Test",    "Verify the connection and schema only",                       DbDashboard.RunTestAsync),
            new MenuItem("Init",    "Create or replace workdb.json",                              DbDashboard.RunInitAsync),
            new MenuItem("New Run", "Create a new pipeline run and set activeRunId",              DbDashboard.RunNewRunAsync),
            new MenuItem("Clear",   "Clear pipeline working data for a country",                  DbDashboard.RunClearAsync),
            new MenuItem("Reset",   "Full wipe — removes reference data too",                     DbDashboard.RunResetAsync),
        ]),

        new MenuGroup("Misc", "Additional features",
        [
            new MenuItem("Analyse", "Produce reports on countries ZpCode format",                  AnalyseDashboard.RunAsync),
        ]),
    ];
}
