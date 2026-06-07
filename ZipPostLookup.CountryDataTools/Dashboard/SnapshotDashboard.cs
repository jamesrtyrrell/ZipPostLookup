using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class SnapshotDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            DashboardRenderer.RenderHeader("Snapshot");

            AnsiConsole.MarkupLine("  Runs the full export pipeline for [bold]US, CA, MX[/] in sequence:");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Step 1[/]  [cyan]export --target ref --all --curated-only[/]");
            AnsiConsole.MarkupLine("          Back up source-of-truth reference CSVs to");
            AnsiConsole.MarkupLine("          [grey]CountryDataTools/Data/{us|ca|mx}/[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Step 2[/]  [cyan]export --all --curated-only[/]");
            AnsiConsole.MarkupLine("          Export optimised main CSV + ZPI images to");
            AnsiConsole.MarkupLine("          [grey]ZipPostLookup/Data/{us|ca|mx}/[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [grey]This can take several minutes. Stops on first failure.[/]");
            AnsiConsole.WriteLine();

            var choice = MenuPrompt.Show(
                ["run", "back"],
                s => s switch
                {
                    "run"  => "[bold green]▶  Run Snapshot[/]",
                    "back" => "[grey]← Back[/]",
                    _      => s,
                },
                escapeReturns: "back");

            if (choice == "back")
                break;

            DashboardRenderer.RenderHeader("Snapshot › Running");

            // Pass --yes so the handler skips its own plain confirm — we already confirmed above.
            var exitCode = await SnapshotCommand.RunAsync(["--yes"]);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(exitCode == 0
                ? "[green]  ✓ Snapshot complete[/]"
                : $"[red]  ✗ Snapshot failed (exit {exitCode})[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
            Console.ReadKey(intercept: true);
        }

        return 0;
    }
}
