using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Commands.Display;

/// <summary>
/// Full CLI help text for CountryDataTools.
/// Printed on --help / -h / unknown command. Kept here so Program.cs stays a pure router.
/// </summary>
public static class HelpText
{
    public static void Print()
    {
        Console.WriteLine();
        AnsiConsole.MarkupLine("[bold]ZipPostLookup.CountryDataTools[/] — postal code data preparation toolkit");
        Console.WriteLine();

        AnsiConsole.MarkupLine("[bold underline]USAGE[/]");
        Console.WriteLine("  countrydatatools <command> [subcommand] [options]");
        Console.WriteLine();

        AnsiConsole.MarkupLine("[bold underline]COMMANDS[/]");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]ingest[/]          Load data into the working database.");
        Console.WriteLine("    ref           --country XX [--all] [--force] [--info-only]");
        Console.WriteLine("                    Seed data.country_info and data.reference from");
        Console.WriteLine("                    the embedded CSV.");
        Console.WriteLine("    candidate     <file.csv> --country XX");
        Console.WriteLine("                    Import a candidate CSV against verified reference");
        Console.WriteLine("                    data. New codes inserted; conflicts go to");
        Console.WriteLine("                    codes.discrepancies.");
        Console.WriteLine("    coords        --source <file.csv> [--country XX] [--batch N] [--dry-run]");
        Console.WriteLine("                    Bulk-resolve timezones from a ZIP,LAT,LNG or");
        Console.WriteLine("                    ZIP,CITY,STATE,LAT,LNG CSV via GeoTimeZone.");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]validate[/]        <file.csv> --country XX [[--report out.txt]] [[--no-prompts]]");
        Console.WriteLine("                    Validate a CSV then guide through fix and extract");
        Console.WriteLine("                    interactively. Pass --no-prompts to run all steps");
        Console.WriteLine("                    automatically (for CI/scripting).");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]fix[/]             <file.csv> --country XX [[--out path.csv]]");
        Console.WriteLine("                    Auto-resolve fixable issues in a candidate CSV: duplicates,");
        Console.WriteLine("                    boolean normalisation, casing, timezone conversion. Blocks on");
        Console.WriteLine("                    structural errors — run 'extractissues' first if any exist.");
        Console.WriteLine("                    Writes back to the same file unless --out is specified.");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]extractissues[/]   <file.csv> --country XX [[--out extracted.csv]]");
        Console.WriteLine("                    Find all rows with at least one structural error, write them to");
        Console.WriteLine("                    a sidecar file (default: {basename}-extracted-records.csv),");
        Console.WriteLine("                    and remove them from the source. Re-validates the cleaned source.");
        Console.WriteLine("                    Fixable issues are left for 'fix' to resolve.");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]enrich[/]          Enrich discrepancies or reference data via external sources.");
        Console.WriteLine("    candidates    --country XX [--run ID] [--limit 100] [--dry-run]");
        Console.WriteLine("             or   --all        [--limit 100] [--dry-run]");
        Console.WriteLine("                    Resolve unresolved discrepancies via Zippopotam.us.");
        Console.WriteLine("    ref           --provider zippopotam --country XX [--limit 100]");
        Console.WriteLine("                    Enrich data.reference rows directly via a provider.");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]export[/]          --country XX [[--target ref|main|zpi]] [[--output path]] [[--curated-only]] [[--uncompressed]]");
        Console.WriteLine("           or:    --all [--target ref] [--curated-only]");
        Console.WriteLine("                    Export data.reference to CSV or a binary frozen image.");
        AnsiConsole.MarkupLine("                    [grey]--target ref[/]   Full ReferenceDataCSV (all curation flags preserved)");
        AnsiConsole.MarkupLine("                                   [grey]→ CountryDataTools/Data/{cc}/{cc}.csv.tar.gz[/]");
        Console.WriteLine("                                   Commit after every curation session; used by 'ingest ref'");
        Console.WriteLine("                                   to rebuild the DB.");
        AnsiConsole.MarkupLine("                    [grey]--target main[/]  (default) Optimised ZipPostLookup CSV for embedding.");
        AnsiConsole.MarkupLine("                                   [grey]→ ZipPostLookup/Data/{cc}/{cc}.csv[/]");
        Console.WriteLine("                                   For US, CA, MX: pipeline applied automatically");
        Console.WriteLine("                                   (lat/lng stripped, ranges collapsed, tz/admin1 indexed).");
        Console.WriteLine("                                   Rebuild ZipPostLookup after exporting.");
        AnsiConsole.MarkupLine("                    [grey]--target zpi[/]   Same optimised data as a ZP frozen image (binary MPHF blob).");
        AnsiConsole.MarkupLine("                                   [grey]→ ZipPostLookup/Data/{cc}/{cc}.zpi.br[/]  (Brotli)");
        Console.WriteLine("                                   --uncompressed writes raw {cc}.zpi instead.");
        Console.WriteLine("                                   --from-csv [path] rebuilds the image from the committed");
        Console.WriteLine("                                   optimised CSV instead of the DB (no Docker/SQL needed).");
        AnsiConsole.MarkupLine("                    [grey]--all[/]          Run for all pipeline countries (US, CA, MX) in sequence.");
        Console.WriteLine("                                   With --target ref: backs up all reference CSVs.");
        Console.WriteLine("                                   Without --target: exports main + zpi for all countries.");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]analyse[/]         --country XX [[--output report.md]]");
        AnsiConsole.MarkupLine("           or:    --all [[--output dir/]]");
        Console.WriteLine("                    Analyse curated reference data (DataCurated=1 required) and write");
        Console.WriteLine("                    a Markdown report covering dataset overview, size estimates,");
        Console.WriteLine("                    compression opportunities, and AI prompts for each finding.");
        AnsiConsole.MarkupLine("                    --all runs for US, CA, MX; --output treated as directory.");
        AnsiConsole.MarkupLine("                    [grey]Output: DataAnalysis/{cc}-analysis-{date}.md[/]");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]integrity[/]       --country XX [[--tests N]] [[--output report.md]]");
        AnsiConsole.MarkupLine("           or:    --all [[--tests N]]");
        Console.WriteLine("                    Verify that ZipPostLookup returns correct name, timezone, and");
        Console.WriteLine("                    admin info for a random sample of codes drawn from curated");
        Console.WriteLine("                    reference data. Run after every export/rebuild to confirm the");
        Console.WriteLine("                    embedded CSV matches the source of truth.");
        AnsiConsole.MarkupLine("                    --all runs for US, CA, MX in sequence. Default: 1000 tests.");
        AnsiConsole.MarkupLine("                    [grey]Output: {cc}-integrity-{date}.md[/]");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]convert[/]         <file.tsv> [[--country XX]] [[--output path/to/output.csv]] [[--no-prompts]]");
        Console.WriteLine("                    Convert a GeoNames or OpenStreetMap TSV to candidate CSV format.");
        Console.WriteLine("                    Format is auto-detected; output written as {cc}-candidate.csv");
        Console.WriteLine("                    alongside the source file. Timezone set to '---' — resolve");
        Console.WriteLine("                    afterwards with 'ingest coords'.");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]snapshot[/]        [[--yes]] Run the full export pipeline for all countries in one command.");
        Console.WriteLine("                    Steps (in order):");
        Console.WriteLine("                      1. export --target ref --all");
        Console.WriteLine("                      2. export --all --curated-only");
        Console.WriteLine("                    Prompts for confirmation before starting (skip with --yes).");
        Console.WriteLine("                    Stops on first failure and prints recovery guidance. On success,");
        Console.WriteLine("                    prints recommended next steps (integrity, build, test, commit).");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]autopromote[/]     --country XX [[--dry-run]]");
        AnsiConsole.MarkupLine("           or:    --all [[--dry-run]]");
        Console.WriteLine("                    Auto-promote candidate aliases from the Gold Name-Discrepancy");
        Console.WriteLine("                    backlog using PlaceNameNormalizer equivalence matching.");
        Console.WriteLine("                    Phase 3 of Gold Name-Discrepancy Backlog Resolution.");
        Console.WriteLine();

        AnsiConsole.MarkupLine("  [bold cyan]db[/]              Manage the working database connection.");
        Console.WriteLine("    init          --country XX --connection \"Server=...\"");
        Console.WriteLine("    status        Show config, connection, and recent runs.");
        Console.WriteLine("    newrun        --source <file>   Create a new run.");
        Console.WriteLine("    test          Verify connection and schema only.");
        Console.WriteLine();

        AnsiConsole.MarkupLine("[bold underline]TYPICAL WORKFLOW[/]");
        Console.WriteLine("  1.  countrydatatools db init          --country US --connection \"Server=...\"");
        Console.WriteLine("  2.  countrydatatools ingest ref       --country US");
        Console.WriteLine("  3.  countrydatatools convert          us-geonames.tsv");
        Console.WriteLine("  4.  countrydatatools validate         us-candidate.csv --country US");
        Console.WriteLine("  5.  countrydatatools ingest candidate us-candidate.csv --country US");
        Console.WriteLine("  6.  countrydatatools enrich candidates --country US --limit 500");
        AnsiConsole.MarkupLine("      [grey]→ repeat until all discrepancies resolved[/]");
        Console.WriteLine("  7.  countrydatatools analyse          --country US");
        Console.WriteLine("  8.  countrydatatools export           --country US --curated-only --target ref");
        AnsiConsole.MarkupLine("      [grey]→ commit CountryDataTools/Data/us/us.csv (source of truth)[/]");
        Console.WriteLine("  9.  countrydatatools export           --country US --curated-only");
        AnsiConsole.MarkupLine("      [grey]→ rebuild ZipPostLookup to embed ZipPostLookup/Data/us/us.csv[/]");
        Console.WriteLine(" 10.  countrydatatools integrity        --country US --tests 10000");
        AnsiConsole.MarkupLine("      [grey]→ confirm embedded data matches the DB[/]");
        Console.WriteLine();
    }
}
