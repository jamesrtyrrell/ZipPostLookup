using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

public static class DashboardCommand
{
    private static readonly CommandEntry Exit =
        new("← Exit", "Return to the terminal", () => Task.FromResult(0));

    public static async Task<int> RunAsync(string[] _)
    {
        var commands = BuildCommands();

        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]CountryDataTools — Dashboard[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<CommandEntry>()
                    .Title("Select a command to view its help:")
                    .UseConverter(e => e == Exit
                        ? "[grey]← Exit[/]"
                        : $"[bold cyan]{e.Name,-14}[/]  [grey]{e.Description}[/]")
                    .AddChoices(commands.Append(Exit)));

            if (selected == Exit)
                break;

            if (selected.IsInteractive)
            {
                // Interactive commands manage their own screen, loop, and "press any key".
                await selected.ShowHelp();
            }
            else
            {
                AnsiConsole.Clear();
                AnsiConsole.Write(new Rule($"[bold cyan]{selected.Name}[/]").LeftJustified());
                AnsiConsole.WriteLine();
                await selected.ShowHelp();
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to return to the menu...[/]");
                Console.ReadKey(intercept: true);
            }
        }

        AnsiConsole.Clear();
        return 0;
    }

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
    ];
}
