using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Dashboard;

public static class DashboardCommand
{
    private static readonly CommandEntry Exit =
        new("← Exit", "Return to the terminal", () => Task.FromResult(0));

    public static async Task<int> RunAsync(string[] _)
    {
        var commands = BuildCommands();
        var allItems = commands.Append(Exit).ToList();
        var index    = 0;

        var stats = await DashboardStats.TryLoadAllAsync();

        while (true)
        {
            Render(allItems, index, stats);

            var key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    index = (index - 1 + allItems.Count) % allItems.Count;
                    break;

                case ConsoleKey.DownArrow:
                    index = (index + 1) % allItems.Count;
                    break;

                case ConsoleKey.Enter:
                    var selected = allItems[index];

                    if (ReferenceEquals(selected, Exit))
                        goto done;

                    if (selected.IsInteractive)
                    {
                        await selected.ShowHelp();
                    }
                    else
                    {
                        DashboardRenderer.RenderHeader(selected.Name);
                        await selected.ShowHelp();
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine("[grey]  Press any key to return to the menu...[/]");
                        Console.ReadKey(intercept: true);
                    }

                    // Refresh stats — sub-commands may have changed DB state.
                    stats = await DashboardStats.TryLoadAllAsync();
                    break;

                case ConsoleKey.Escape:
                    goto done;
            }
        }

        done:
        AnsiConsole.Clear();
        return 0;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private static void Render(
        IReadOnlyList<CommandEntry> items, int selectedIndex, List<CountryStatsRow> stats)
    {
        DashboardRenderer.RenderHeader("Dashboard");

        var menuTable = BuildMenuTable(items, selectedIndex);

        if (stats.Count > 0)
            AnsiConsole.Write(new Columns(menuTable, DashboardStats.BuildTable(stats)));
        else
            AnsiConsole.Write(menuTable);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]↑↓ move   Enter select   Esc exit[/]");
    }

    private static Table BuildMenuTable(IReadOnlyList<CommandEntry> items, int selectedIndex)
    {
        var table = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn(""))
            .AddColumn(new TableColumn(""))
            .AddColumn(new TableColumn(""));

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var sel  = i == selectedIndex;
            var ind  = sel ? "[bold green]❯[/]" : " ";

            if (ReferenceEquals(item, Exit))
            {
                table.AddRow(ind, sel ? "[bold white]← Exit[/]" : "[grey]← Exit[/]", "");
            }
            else
            {
                var name = sel
                    ? $"[bold cyan]{item.Name,-14}[/]"
                    : $"[cyan]{item.Name,-14}[/]";
                var desc = sel
                    ? $"[bold white]{item.Description}[/]"
                    : $"[grey]{item.Description}[/]";
                table.AddRow(ind, name, desc);
            }
        }

        return table;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<CommandEntry> BuildCommands() =>
    [
        new("ingest",    "Load data into the working database",              IngestDashboard.RunAsync,    IsInteractive: true),
        new("validate",  "Validate a candidate CSV file",                    ValidateDashboard.RunAsync,  IsInteractive: true),
        new("enrich",    "Enrich discrepancies or reference data via APIs",  EnrichDashboard.RunAsync,    IsInteractive: true),
        new("export",    "Export reference data to CSV or ZP image",         ExportDashboard.RunAsync,    IsInteractive: true),
        new("analyse",   "Analyse curated reference data",                   AnalyseDashboard.RunAsync,   IsInteractive: true),
        new("integrity", "Verify embedded data against reference DB",        IntegrityDashboard.RunAsync, IsInteractive: true),
        new("convert",   "Convert GeoNames / OSM TSV to candidate CSV",      ConvertDashboard.RunAsync,   IsInteractive: true),
        new("snapshot",  "Full export pipeline for all countries",           SnapshotDashboard.RunAsync,  IsInteractive: true),
        new("db",        "Manage the working database connection",           DbDashboard.RunAsync,        IsInteractive: true),
        new("editor",    "Browse and curate uncurated reference codes",      ZpCodeEditorDashboard.RunAsync, IsInteractive: true),
    ];
}
