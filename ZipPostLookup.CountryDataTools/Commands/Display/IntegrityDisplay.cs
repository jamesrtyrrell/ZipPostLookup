using Spectre.Console;
using ZipPostLookup.CountryDataTools.Models.Commands;

namespace ZipPostLookup.CountryDataTools.Commands.Display;

public static class IntegrityDisplay
{
    public static void PrintHeader(
        string cc, string countryName,
        int curatedRows, int distinctCodes, int testsToRun, int requested)
    {
        var testsLabel = testsToRun < requested
            ? $"{testsToRun:N0}  (capped at distinct code count)"
            : $"{testsToRun:N0}";

        CommandDisplay.PrintTable($"Integrity Check — {cc}",
            ("Country",       $"{cc} ({Markup.Escape(countryName)})"),
            ("Curated rows",  $"{curatedRows:N0}"),
            ("Distinct codes", $"{distinctCodes:N0}"),
            ("Tests to run",  testsLabel));
    }

    public static void PrintSummary(string cc, IntegrityCheckSummary s)
    {
        var cleanDefault = s.DefaultTested - s.UniqueDefaultFailed;
        var passRate     = s.DefaultTested > 0
            ? (double)cleanDefault / s.DefaultTested * 100
            : 0;

        AnsiConsole.MarkupLine($"  [bold]{Markup.Escape(cc)} Integrity Check — Results[/]");

        var failColour = s.HasFailures ? "red" : "green";
        var table = new Table()
            .AddColumn("Check")
            .AddColumn(new TableColumn("Tests").RightAligned())
            .AddColumn(new TableColumn("Failures").RightAligned());

        table.AddRow("Default codes tested",        $"{s.DefaultTested:N0}",                          "—");
        table.AddRow("Non-default rows tested",     $"{s.NonDefaultTested:N0}",                       "—");
        table.AddRow("GetByCode",                   $"{s.GetByCodeAttempts:N0}",                      $"{s.GetByCodeFailures:N0}");
        table.AddRow("GetByZip",                    $"{s.GetByZipAttempts:N0}",                       $"{s.GetByZipFailures:N0}");
        table.AddRow("GetAllByCode (default)",      $"{s.GetAllByCodeAttempts:N0}",                   $"{s.GetAllByCodeFailures:N0}");
        table.AddRow("GetAllByCode (non-default)",  $"{s.GetAllByCodeNonDefaultAttempts:N0}",         $"{s.GetAllByCodeNonDefaultFailures:N0}");
        table.AddEmptyRow();
        table.AddRow("[bold]Total failures[/]", "", $"[{failColour}]{s.TotalFailures:N0}[/]");
        table.AddRow("[bold]Unique codes[/]",   "", $"[{failColour}]{s.UniqueFailed:N0}[/]");

        AnsiConsole.Write(table);
        Console.WriteLine();

        if (!s.HasFailures)
        {
            AnsiConsole.MarkupLine(
                $"  [green]✓ All checks passed ({s.DefaultTested:N0} default codes, {s.NonDefaultTested:N0} non-default rows)[/]");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"  [red]✗ {s.UniqueFailed:N0} unique code(s) had failures " +
                $"({passRate:F1}% of default codes clean)[/]");
        }

        Console.WriteLine();
    }
}
