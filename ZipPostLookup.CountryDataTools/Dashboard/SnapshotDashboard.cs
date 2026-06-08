using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class SnapshotDashboard
{
    private sealed record SnapshotPage(
        string Country,
        int    RefCode,
        int    MainCode,
        int    ZpiCode)
    {
        public bool AllPassed => RefCode == 0 && MainCode == 0 && ZpiCode == 0;
    }

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("Snapshot");

            AnsiConsole.MarkupLine("  Runs the full export pipeline for [bold]US, CA, MX[/] in sequence:");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Step 1[/]  [cyan]export --target ref --all [/]");
            AnsiConsole.MarkupLine("          Back up source-of-truth reference CSVs to");
            AnsiConsole.MarkupLine("          [grey]CountryDataTools/Data/{us|ca|mx}/[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Step 2[/]  [cyan]export --all --curated-only[/]");
            AnsiConsole.MarkupLine("          Export optimised main CSV + ZPI images to");
            AnsiConsole.MarkupLine("          [grey]ZipPostLookup/Data/{us|ca|mx}/[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [grey]This can take several minutes. Stops on first failure.[/]");
            AnsiConsole.WriteLine();

            var choice = CdtSelectMenu.Show(
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

            HeaderBar.Render("Snapshot › Running");

            var pages = await RunWithProgressAsync();

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(pages.All(p => p.AllPassed)
                ? "[green]  ✓ Snapshot complete[/]"
                : "[red]  ✗ Snapshot failed[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]  Press any key to browse results...[/]");
            Console.ReadKey(intercept: true);

            BrowseResults(pages);
        }

        return 0;
    }

    // ── Running — per-step progress display ───────────────────────────────────

    private static async Task<List<SnapshotPage>> RunWithProgressAsync()
    {
        var pages = new List<SnapshotPage>();

        foreach (var cc in new[] { "US", "CA", "MX" })
        {
            AnsiConsole.MarkupLine($"  [bold]{cc}[/]");

            var refCode  = await RunStepAsync(cc, "ref ", "backup CSV          ");

            int mainCode, zpiCode;
            if (refCode == 0)
            {
                mainCode = await RunStepAsync(cc, "main", "optimised CSV       ");
                zpiCode  = mainCode == 0 ? await RunStepAsync(cc, "zpi ", "frozen binary image ") : -1;
                if (zpiCode == -1) PrintSkipped("zpi ", "frozen binary image ");
            }
            else
            {
                mainCode = -1;
                zpiCode  = -1;
                PrintSkipped("main", "optimised CSV       ");
                PrintSkipped("zpi ", "frozen binary image ");
            }

            pages.Add(new(cc, refCode, mainCode, zpiCode));
            AnsiConsole.WriteLine();
        }

        return pages;
    }

    private static async Task<int> RunStepAsync(string cc, string target, string label)
    {
        var row = Console.CursorTop;
        AnsiConsole.MarkupLine($"    {target}  {label}[grey]running…[/]");

        int code;
        var origOut   = Console.Out;
        var origErr   = Console.Error;
        var origAnsi  = AnsiConsole.Console;

        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null)
        });

        try
        {
            code = await ExportReferenceCommand.RunAsync(BuildArgs(cc, target.Trim()));
        }
        catch (Exception)
        {
            code = 1;
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
            AnsiConsole.Console = origAnsi;
        }

        Console.SetCursorPosition(0, row);
        var icon = code == 0 ? "[green]✓[/]" : "[red]✗[/]";
        AnsiConsole.MarkupLine($"    {target}  {label}{icon}");

        return code;
    }

    private static void PrintSkipped(string target, string label) =>
        AnsiConsole.MarkupLine($"    {target}  {label}[grey]—[/]");

    private static string[] BuildArgs(string cc, string target)
    {
        var args = new List<string> { "--country", cc };
        if (target != "main") args.AddRange(["--target", target]);
        if (target != "ref") args.Add("--curated-only");
        return [.. args];
    }

    // ── Browse results (← → per-country navigation) ──────────────────────────

    private static void BrowseResults(List<SnapshotPage> pages)
    {
        if (pages.Count == 0) return;

        var index = 0;
        while (true)
        {
            var page        = pages[index];
            var statusLabel = page.AllPassed ? "[green]✓ passed[/]" : "[red]✗ failed[/]";
            var prevLabel   = index > 0                     ? $"[bold]← {pages[index - 1].Country}[/]" : "[grey]←[/]";
            var nextLabel   = index < pages.Count - 1       ? $"[bold]{pages[index + 1].Country} →[/]" : "[grey]→[/]";

            HeaderBar.Render($"Snapshot › {page.Country}  {statusLabel}");

            CdtCommandMenu.Render(
                $"  {prevLabel}    ({index + 1}/{pages.Count})    {nextLabel}    [grey]Esc: back[/]");
            AnsiConsole.WriteLine();

            PrintSummaryTable(page);

            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.LeftArrow  when index > 0:               index--; break;
                case ConsoleKey.RightArrow when index < pages.Count - 1: index++; break;
                case ConsoleKey.Escape:                                   return;
            }
        }
    }

    private static void PrintSummaryTable(SnapshotPage page)
    {
        var table = new Table()
            .AddColumn($"[bold]{page.Country} — Snapshot[/]")
            .AddColumn(new TableColumn("Status").Centered());

        table.AddRow("ref  — backup CSV (source of truth)", StatusIcon(page.RefCode));
        table.AddRow("main — optimised CSV",                StatusIcon(page.MainCode));
        table.AddRow("zpi  — frozen binary image",          StatusIcon(page.ZpiCode));

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string StatusIcon(int code) => code switch
    {
        0    => "[green]✓[/]",
        -1   => "[grey]—[/]",
        _    => "[red]✗[/]",
    };
}
