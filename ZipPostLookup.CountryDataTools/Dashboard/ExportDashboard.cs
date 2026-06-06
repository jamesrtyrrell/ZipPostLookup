using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class ExportDashboard
{
    private sealed record Target(string Key, string Label, string Desc);

    private static readonly Target TargetRef  = new("ref",  "ref",  "Source-of-truth reference CSV → CountryDataTools/Data/{cc}/");
    private static readonly Target TargetMain = new("main", "main", "Optimised library CSV → ZipPostLookup/Data/{cc}/");
    private static readonly Target TargetZpi  = new("zpi",  "zpi",  "Frozen binary ZPI image → ZipPostLookup/Data/{cc}/");
    private static readonly Target TargetBack = new("back", "← Back", "");

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]Export[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var target = AnsiConsole.Prompt(
                new SelectionPrompt<Target>()
                    .Title("  Export target:")
                    .UseConverter(t => t == TargetBack
                        ? "[grey]← Back[/]"
                        : $"[bold cyan]{t.Label,-8}[/]  [grey]{t.Desc}[/]")
                    .AddChoices(TargetRef, TargetMain, TargetZpi, TargetBack));

            if (target == TargetBack) break;

            var countryChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("  Country:")
                    .UseConverter(s => s == "All (US + CA + MX)"
                        ? $"[bold cyan]{"All",-10}[/]  [grey]Run all three in sequence[/]"
                        : $"[bold cyan]{s}[/]")
                    .AddChoices("US", "CA", "MX", "All (US + CA + MX)"));

            var curatedOnly  = AnsiConsole.Confirm("  --curated-only (skip non-curated rows)?", true);
            var uncompressed = target == TargetZpi
                && AnsiConsole.Confirm("  --uncompressed (write raw .zpi instead of .zpi.br)?", false);

            var args = new List<string> { "--target", target.Key };
            if (countryChoice.StartsWith("All")) args.Add("--all");
            else args.AddRange(["--country", countryChoice]);
            if (curatedOnly)  args.Add("--curated-only");
            if (uncompressed) args.Add("--uncompressed");

            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule($"[bold cyan]export --target {target.Key}[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var exitCode = await ExportCommand.RunAsync([.. args]);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(exitCode == 0
                ? "[green]  ✓ Export complete[/]"
                : $"[red]  ✗ Exited with code {exitCode}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
            Console.ReadKey(intercept: true);
        }

        return 0;
    }
}
