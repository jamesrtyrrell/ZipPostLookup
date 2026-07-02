namespace ZipPostLookup.CountryDataTools.Commands;

/// <summary>
/// CountryDataTools ingest &lt;subcommand&gt; [options]
///
/// Subcommands:
///   ref        --country XX [--all] [--force] [--info-only]
///   candidate  &lt;file.csv&gt; --country XX
///   coords     --source &lt;file.csv&gt; [--country XX] [--batch N] [--dry-run]
/// </summary>
public static class IngestCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "ref" => await Handlers.ImportReferenceDataCommand.RunAsync(args[1..]),
            "candidate" => await Handlers.ImportCandidatesCommand.RunAsync(args[1..]),
            "coords" => await Handlers.EnrichReferenceFromCoordinatesCommand.RunAsync(args[1..]),
            "auto" => await Handlers.AutoImportCommand.RunAsync(args[1..]),
            _ => Unknown(args[0])
        };
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"Unknown ingest subcommand: {sub}");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
                          Usage: countrydatatools ingest <subcommand> [options]

                            ref        --country XX [--all] [--force] [--info-only]
                                         Seed data.country_info and data.reference from the embedded CSV.
                            candidate  <file.csv> --country XX
                                         Import a candidate CSV against verified reference data.
                            coords     --source <file.csv> [--country XX] [--batch 1000] [--dry-run]
                                         Bulk-resolve timezones from a ZIP,LAT,LNG or ZIP,CITY,STATE,LAT,LNG CSV.
                            auto       <file> [--sample-rows N] [--min-hit-rate N] [--country XX] [--dry-run] [--no-llm] [--no-ui]
                                         Auto-detect file format and import postal code data (CSV/TSV/Excel/JSON).
                          """);
}