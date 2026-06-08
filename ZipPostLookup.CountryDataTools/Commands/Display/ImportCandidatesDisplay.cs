using Spectre.Console;
using ZipPostLookup.CountryDataTools.Models.Commands;

namespace ZipPostLookup.CountryDataTools.Commands.Display;

public static class ImportCandidatesDisplay
{
    public static void PrintSummary(ImportCounters counters)
    {
        var table = new Table()
            .AddColumn("Result")
            .AddColumn(new TableColumn("Count").RightAligned());

        table.AddRow("New codes inserted",     $"{counters.NewCodes:N0}");
        table.AddRow("Already in reference",   $"{counters.AlreadyClean:N0}");
        table.AddRow("Coords enriched",        $"{counters.CoordsEnriched:N0}");
        table.AddRow("Name discrepancies",     counters.NameDiscrepancies     > 0 ? $"[yellow]{counters.NameDiscrepancies:N0}[/]"     : "0");
        table.AddRow("Timezone discrepancies", counters.TimezoneDiscrepancies > 0 ? $"[yellow]{counters.TimezoneDiscrepancies:N0}[/]" : "0");
        table.AddRow("Auto-rejected",          $"{counters.AutoRejected:N0}");
        table.AddRow("Special codes bypassed", $"[grey]{counters.SpecialCodes:N0}[/]");
        table.AddRow("Abbreviation matches",   $"[grey]{counters.AbbreviationMatches:N0}[/]");
        table.AddRow("Curated — skipped",      $"[grey]{counters.CuratedSkipped:N0}[/]");
        table.AddRow("Flagged — suppressed",   counters.FlaggedSkipped > 0 ? $"[red]{counters.FlaggedSkipped:N0}[/]" : "0");

        AnsiConsole.Write(table);
    }
}
