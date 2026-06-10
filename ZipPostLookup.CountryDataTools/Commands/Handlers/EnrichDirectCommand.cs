using Dapper;
using Microsoft.Data.SqlClient;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Database;
using ZipPostLookup.CountryDataTools.Database.Repositories;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Enrichment;
using ZipPostLookup.CountryDataTools.Enrichment.Api;
using ZipPostLookup.CountryDataTools.Models.Commands;
using ZipPostLookup.CountryDataTools.Utilities;
using ZipPostLookup.CountryDataTools.Validation;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools enrich direct --country US|CA|MX [--limit N] [--dry-run]
///                                 --all              [--limit N] [--dry-run]
///
/// Directly enriches uncurated rows already in data.Reference (Curated = 0)
/// without requiring a pipeline run or source file import. Use this to backfill
/// reference data that was never enriched via the discrepancy pipeline.
///
/// Per zip:
///   1. Query data.Reference WHERE Curated = 0 for distinct ZpCodes
///   2. Filter special-domain codes (APO/FPO/PRS etc.)
///   3. Call configured APIs round-robin (same router and API pool as enrichcandidates)
///   4. Found         → UpdateReferenceAsync: sets TimezoneChecked / NameChecked / admin / coords
///   5. NotFound      → counted; row remains Curated=0 and will be retried next run
///   6. TransientError → counted; row retried next run
/// </summary>
public static class EnrichDirectCommand
{
    private const int DelayMs = 0_800;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        if (!TryParseArgs(args, out var country, out var limit, out var dryRun, out var all))
        {
            PrintUsage();
            return 2;
        }

        if (all)
            return await CountryRunner.ForEachWithRuleAsync(cc => RunAsync(["--country", cc,
                "--limit", limit.ToString(),
                .. (dryRun ? new[] { "--dry-run" } : Array.Empty<string>())]));

        WorkDbContext db;
        try
        {
            db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"  ✗ DB connection failed: {ex.Message}");
            return 1;
        }

        await using var conn = (SqlConnection)db.GetFactory().CreateConnection();

        // Load all uncurated codes with their level-1 admin code for territory routing.
        var uncurated = (await conn.QueryAsync<(string ZpCode, string Admin1Code)>(
            CommonQueries.GetUncuratedReferenceCodes,
            new { CountryId = country.ToUpperInvariant() })).ToList();

        // Filter special-domain codes (APO/FPO/DPO/territory) — same logic as enrichcandidates.
        var rules = CountryRulesFactory.For(country);
        var specialCodeSet = uncurated
            .Where(r => rules.IsEnrichmentSkipped(r.ZpCode))
            .Select(r => r.ZpCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var enrichable = uncurated
            .Where(r => !specialCodeSet.Contains(r.ZpCode))
            .ToList();

        EnrichCandidatesDisplay.PrintDirectHeader(
            country,
            uncurated.Count, specialCodeSet.Count, enrichable.Count,
            limit, DelayMs / 1000, dryRun);
        Console.WriteLine();

        if (enrichable.Count == 0)
        {
            Console.WriteLine("  No uncurated codes — nothing to enrich.");
            return 0;
        }

        var batch = enrichable.Take(limit).ToList();

        if (dryRun)
        {
            foreach (var (code, admin) in batch)
                AnsiConsole.MarkupLine($"  [grey][[dry-run]] Would enrich {Markup.Escape(code),-12}  admin: {Markup.Escape(admin)}[/]");
            return 0;
        }

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ZipPostLookup.CountryDataTools/1.0 (direct enrichment)");

        var counters = new EnrichCandidateCounters
        {
            SpecialCodes = specialCodeSet.Count
        };

        var apiKeys = ApiKeysConfig.TryLoad(db.RepoRoot);
        var apis    = EnrichmentApiFactory.GetApisForCountry(country, http, apiKeys);
        var router  = new RoundRobinEnrichmentRouter(apis);

        // Seed router with persisted period counts so limits are enforced across sessions.
        var periodUsage = await ApiUsageRepository.GetCurrentPeriodUsageAsync(conn);
        foreach (var (name, count) in periodUsage)
            router.SeedCallCount(name, count);

        foreach (var api in apis)
            counters.InitDailyUsage(api.Name,
                periodUsage.GetValueOrDefault(api.Name, 0),
                api.DailyLimit ?? api.MonthlyLimit);

        // Per-code admin1 is preloaded with the uncurated query.
        var adminByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (zip, admin) in batch)
            adminByCode[zip] = admin;

        // Run-scoped unfound log in <repo>/Logs — records codes downgraded to unfound for an
        // over-long place name (>100 chars), appended as they arrive.
        var unfoundCount = 0;
        string? unfoundLogPath = null;
        try
        {
            var logDir = Path.Combine(db.RepoRoot, "Logs");
            Directory.CreateDirectory(logDir);
            unfoundLogPath = Path.Combine(logDir, $"enrich-direct-unfound_{country.ToUpperInvariant()}_{DateTime.UtcNow:yyyyMMdd}.log");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ Could not prepare unfound log directory: {ex.Message}");
        }

        var userCancelled = await EnrichmentEngine.RunAsync(new EnrichmentRun
        {
            Conn          = conn,
            Country       = country,
            Rules         = rules,
            Batch         = batch.Select(b => b.ZpCode).ToList(),
            Counters      = counters,
            Router        = router,
            Apis          = apis,
            DelayMs       = DelayMs,
            IsDirectMode  = true,
            GetStateAsync = code => Task.FromResult<string?>(adminByCode.GetValueOrDefault(code, "")),
            PersistAsync  = async (c, tx, items) =>
            {
                foreach (var item in items)
                {
                    // NotFound: no DB action — row stays Curated=0, retried next run.
                    if (item.Outcome != FetchOutcome.Found) continue;
                    var newName = await ReferenceEnrichmentHelper.UpdateReferenceAsync(
                        c, country, item.Zip, item.Result!, tx, rules.ResolveAdmin1(item.Zip));
                    if (newName) counters.NewNamesInserted++;
                }
            },
            OnLongPlaceNameAsync = async (code, api, name) =>
            {
                if (unfoundLogPath is null) return;
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}  {country.ToUpperInvariant()}  {code,-10}  unfound (name too long: {name.Length} chars)  via {api,-14}  {name}";
                try
                {
                    await File.AppendAllLinesAsync(unfoundLogPath, [line]);
                    unfoundCount++;
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"  ✗ Failed to append unfound log: {ex.Message}");
                }
            },
        });

        Console.WriteLine();
        if (userCancelled)
            AnsiConsole.MarkupLine("[yellow]  ⚠ Enrichment stopped by user.[/]");
        else
            Console.WriteLine("Enrichment complete:");
        EnrichCandidatesDisplay.PrintSummary(counters, isDirectMode: true);

        var gold = await GoldCertifier.CertifyAsync(conn, country);
        if (gold.Failed)
            AnsiConsole.MarkupLine($"  [grey](Gold certification skipped: {Markup.Escape(gold.Error!)})[/]");
        else if (gold.Certified > 0)
            AnsiConsole.MarkupLine($"  [bold yellow]⭐ Gold: {gold.Certified:N0} new code(s) certified[/]");

        // Over-long names downgraded to unfound were appended to the run log; point the user at it.
        if (unfoundCount > 0 && unfoundLogPath != null)
        {
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                $"  [grey]{unfoundCount} over-long name(s) logged as unfound to[/] {Markup.Escape(unfoundLogPath)}");
        }

        int remaining = enrichable.Count - batch.Count;
        if (remaining > 0)
        {
            var etaMinutes = remaining * (DelayMs / 1000.0) / 60;
            Console.WriteLine();
            Console.WriteLine($"  {remaining:N0} uncurated codes remaining — " +
                              $"run again to continue (~{etaMinutes:F0} min at {limit}/run).");
        }

        return 0;
    }

    private static bool TryParseArgs(
        string[] args, out string country, out int limit, out bool dryRun, out bool all)
    {
        country = args.OptionValue("--country", rejectFlagValue: true) ?? "";
        limit   = args.IntOption("--limit", 100, min: 1);
        dryRun  = args.HasFlag("--dry-run");
        all     = args.HasFlag("--all");

        return all || !string.IsNullOrWhiteSpace(country);
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools enrich direct --country US|CA|MX [--limit N] [--dry-run]
                                                  --all              [--limit N] [--dry-run]

              --country   Country code: US, CA, or MX
              --all       Process all three countries in sequence (US then CA then MX)
              --limit     Max codes to enrich this run (default: 100)
              --dry-run   List codes that would be enriched without calling any APIs
            """);
}
