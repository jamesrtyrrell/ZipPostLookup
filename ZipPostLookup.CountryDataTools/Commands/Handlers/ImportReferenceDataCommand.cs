using Spectre.Console;
using Z.Dapper.Plus;
using ZipPostLookup.Core;
using ZipPostLookup.CountryDataTools.Database;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using CurationStatus = ZipPostLookup.CountryDataTools.Models.Enums.CurationStatus;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools importref --country US [--all] [--force] [--info-only]
///
/// Seeds the working database with reference data from two sources:
///
///   1. data.country_info  — populated from the embedded countries.json
///      (CountryInfo records: regex, curation status, notes, etc.)
///
///   2. [data].[reference]     — populated from the on-disk source-of-truth backup
///      ZipPostLookup.CountryDataTools/Data/{cc}/{cc}.csv.tar.gz (gzip-compressed,
///      expanded in-memory by EmbeddedCsvLoader; plain {cc}.csv fallback)
///      (the existing postal code entries that new candidates are compared against)
///
/// OPTIONS
///   --country XX   Import a single country (required unless --all is passed)
///   --all          Import all countries defined in countries.json
///   --force        Re-import even if [data].[reference] already has rows for
///                  this country (drops existing rows first)
///   --info-only    Seed data.country_info only; skip [data].[reference] rows
/// </summary>
public static class ImportReferenceDataCommand
{
    public sealed record Options(string Country = "", bool Force = false, bool InfoOnly = false, bool All = false);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        var country  = args.OptionValue("--country", rejectFlagValue: true) ?? "";
        var all      = args.HasFlag("--all");
        var force    = args.HasFlag("--force");
        var infoOnly = args.HasFlag("--info-only");

        if (!all && string.IsNullOrWhiteSpace(country))
        {
            await Console.Error.WriteLineAsync("  Specify --country XX or --all.");
            PrintUsage();
            return 2;
        }

        return await RunAsync(new Options(country, force, infoOnly, all));
    }

    public static async Task<int> RunAsync(Options opts)
    {
        // --- Load workdb context ---
        WorkDbContext db;
        try
        {
            db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"  ✗ Cannot connect to working database: {ex.Message}");
            await Console.Error.WriteLineAsync("  Run 'countrydatatools workdb init' to configure the connection.");
            return 1;
        }

        // --- Determine which countries to import ---
        // CountryInfoSource is internal to ZipPostLookup — access via the public
        // CountryInfoRegistry facade instead, which exposes the same data.
        var allKnown = CountryInfoRegistry.All;
        var targets = opts.All
            ? allKnown.Keys.ToList()
            : [opts.Country.ToUpperInvariant()];

        int exitCode = 0;

        foreach (var cc in targets)
        {
            Console.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(cc)}[/]").LeftJustified());
            var result = await ImportCountryAsync(db, cc, opts.Force, opts.InfoOnly);
            if (result != 0) exitCode = result;
        }

        Console.WriteLine();
        Console.WriteLine(exitCode == 0 ? "Import complete." : "Import completed with errors.");
        return exitCode;
    }

    // -------------------------------------------------------------------------

    private static async Task<int> ImportCountryAsync(
        WorkDbContext db, string countryCode, bool force, bool infoOnly)
    {
        var dataService = new DataServices(db.GetFactory());

        // --- 1. Upsert country_info from countries.json ---
        var countryInfoList = EmbeddedCsvLoader.GetCountriesInfo();
        if (countryInfoList == null)
        {
            await Console.Error.WriteLineAsync($"  ✗ Countries.json is not embedded in the assembly.");
            return 1;
        }
        var country = countryInfoList.FirstOrDefault(c => c.CountryId == countryCode);
        if (country is null)
        {
            await Console.Error.WriteLineAsync($"  ✗ {countryCode} is not defined in countries.json.");
            await Console.Error.WriteLineAsync($"  Add an entry to countries.json and rebuild the assembly, or");
            await Console.Error.WriteLineAsync($"  manually insert a row into data.country_info in SSMS.");
            return 1;
        }

        
        await dataService.MergeDataRecordsAsync([new DataCountryInfo(country)]);
        Console.WriteLine($"  ✓ data.country_info upserted for {countryCode}");
        Console.WriteLine($"    Name    : {country.CountryName}");
        Console.WriteLine($"    Regex   : {country.CodeRegex ?? "(none)"}");
        Console.WriteLine($"    Status  : {country.CurationStatus}");
        if (country.ConstrainedRegex != null)
            Console.WriteLine($"    Constrained regex: {country.ConstrainedRegex}");
        if (country.Notes != null)
            Console.WriteLine($"    Notes   : {Truncate(country.Notes, 80)}");

        if (infoOnly)
        {
            Console.WriteLine($"  --info-only: skipping data.reference import.");
            return 0;
        }

        // --- 1.1 Upsert Admin levels from cc_info.json --- 
        var adminLevels = EmbeddedCsvLoader.LoadAdminLevels(countryCode);
        if (adminLevels.Any())
        {
            using var conn = db.GetFactory().CreateConnection();
            await conn.BulkMergeAsync(adminLevels);
        }

        // --- 2. Import [data].[reference] from the on-disk reference backup ({cc}.csv.tar.gz) ---
        var hasData = await db.Reference.HasDataAsync(countryCode);

        if (hasData && !force)
        {
            var existing = await db.Reference.GetCountAsync(countryCode);
            Console.WriteLine($"  ℹ data.reference already has {existing:N0} rows for {countryCode}.");
            Console.WriteLine($"  Pass --force to drop and re-import.");
            return 0;
        }

        if (hasData && force)
        {
            Console.WriteLine($"  --force: clearing existing data.reference rows for {countryCode}…");
            await db.Reference.DeleteAllAsync(countryCode);
            Console.WriteLine($"  Existing rows cleared.");
            using (var goldConn = db.GetFactory().CreateConnection())
            {
                if (await GoldCertifier.TryRevokeAllAsync(goldConn, countryCode))
                    Console.WriteLine($"  Gold certifications cleared.");
            }
        }

        // Check that a reference CSV exists on disk before attempting the load
        bool csvExists = BuiltInCsvExists(countryCode);
        if (!csvExists)
        {
            var cc = countryCode.ToLowerInvariant();
            if (country.CurationStatus == CurationStatus.NoData)
            {
                Console.WriteLine($"  ⚠ No reference CSV for {countryCode} (status = NoData) — skipping.");
                Console.WriteLine($"  Run 'export --country {countryCode} --target ref' to create");
                Console.WriteLine($"  Data/{cc}/{cc}.csv.tar.gz, then re-run ingest.");
                return 0;
            }

            await Console.Error.WriteLineAsync($"  ✗ No reference CSV found for {countryCode} but curation_status is not NoData.");
            return 1;
        }

        Console.WriteLine($"  Loading reference CSV (tar.gz) into data.reference…");
        await db.Reference.LoadFromEmbeddedCsvAsync(countryCode);

        var count = await db.Reference.GetCountAsync(countryCode);
        Console.WriteLine($"  ✓ data.reference: {count:N0} rows loaded for {countryCode}");

        using (var goldConn = db.GetFactory().CreateConnection())
        {
            var gold = await GoldCertifier.CertifyAsync(goldConn, countryCode);
            if (!gold.Failed)
                Console.WriteLine(gold.Certified > 0
                    ? $"  ✓ Gold: {gold.Certified:N0} code(s) certified"
                    : $"  ✓ Gold: no codes eligible yet");
        }

        return 0;
    }

    // -------------------------------------------------------------------------

    private static bool BuiltInCsvExists(string countryCode) =>
        EmbeddedCsvLoader.Exists(countryCode);

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools importref --country US [--all] [--force] [--info-only]

              --country XX   Import a single country
              --all          Import all countries in countries.json
              --force        Re-import even if data.reference already has rows
              --info-only    Seed data.country_info only; skip reference rows
            """);

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}