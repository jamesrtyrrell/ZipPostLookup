namespace ZipPostLookup.CountryDataTools.Commands;

/// <summary>
/// CountryDataTools enrich &lt;subcommand&gt; [options]
///
/// Subcommands:
///   candidates  --country XX [--run ID] [--limit 100] [--dry-run]
///   direct      --country XX|--all      [--limit 100] [--dry-run]
///   ref         --source &lt;file.csv&gt; | --provider zippopotam [options]
/// </summary>
public static class EnrichCommand
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
            "candidates" => await Handlers.EnrichCandidatesCommand.RunAsync(args[1..]),
            "direct"     => await Handlers.EnrichDirectCommand.RunAsync(args[1..]),
            "ref"        => await DispatchRefEnrichment(args[1..]),
            _ => Unknown(args[0])
        };
    }

    /// <summary>
    /// Routes enrich ref based on whether --source (coords file) or --provider
    /// (API-based enrichment) was supplied.
    /// </summary>
    private static async Task<int> DispatchRefEnrichment(string[] args)
    {
        // --source → coordinate file enrichment
        if (args.Any(a => a.Equals("--source", StringComparison.OrdinalIgnoreCase)))
        {
            return await Handlers.EnrichReferenceFromCoordinatesCommand.RunAsync(args);
        }

        // --provider → API-based enrichment (currently only zippopotam)
        var providerIdx = Array.FindIndex(args,
            a => a.Equals("--provider", StringComparison.OrdinalIgnoreCase));

        var provider = providerIdx >= 0 && providerIdx + 1 < args.Length
            ? args[providerIdx + 1].ToLowerInvariant()
            : "zippopotam";

        return provider switch
        {
            "zippopotam" => await Handlers.EnrichCandidatesCommand.RunAsync(args),
            _ => UnknownProvider(provider)
        };
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"Unknown enrich subcommand: {sub}");
        PrintUsage();
        return 2;
    }

    private static int UnknownProvider(string provider)
    {
        Console.Error.WriteLine($"Unknown enrichment provider: {provider}");
        Console.Error.WriteLine("Supported providers: zippopotam");
        return 2;
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools enrich <subcommand> [options]

              candidates  --country XX [--run ID] [--limit 100] [--dry-run]
                            Resolve discrepancies in a pipeline run via the API pool.
              direct      --country XX|--all [--limit 100] [--dry-run]
                            Backfill uncurated rows in data.Reference directly via APIs.
                            No source file or pipeline run required.
              ref         --source <file.csv> [--country XX] [--batch N] [--dry-run]
                            Enrich data.reference from a coordinates CSV.
              ref         --provider zippopotam --country XX [--limit 100]
                            Enrich data.reference via an API provider.
            """);
}