using Dapper;
using Microsoft.Data.SqlClient;
using Dapper.Contrib.Extensions;
using ZipPostLookup.CountryDataTools.Database;
using ZipPostLookup.CountryDataTools.Database.Repositories;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Models.Counters;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.CountryRules;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Enrichment;
using ZipPostLookup.CountryDataTools.Enrichment.Api;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools enrichcandidates --country US [--limit 100] [--dry-run]
///
/// For each unresolved discrepancy across all runs for the country, queries the
/// enrichment API round-robin (Zippopotam.us, Ziptastic, etc.) for the zip code
/// and resolves Name, state, state_name, and timezone authoritatively.
///
/// Per zip:
///   1. Call configured APIs round-robin (429 removes an API from rotation)
///   2. Title-case the returned place name → authoritative Name
///   3. state abbreviation → state, full state name → state_name
///   4. lat/lon → GeoTimeZone → IANA timezone (when API provides coordinates)
///   5. Set OverrideValue on all discrepancy field rows for this zip
///   6. Set AcceptIncoming=1, Process=1 (auto-resolved)
///   7. Update [data].[reference] with verified values + TimezoneChecked=1, NameChecked=1
///   8. If the new PlaceName not in [data].[reference] → insert it
///   9. 404 → mark [codes].[candidate] row as unfound
///  10. Transient error → leave in place for next session
///
/// Default limit: 100 zips per session. At 2s delay that's ~3 minutes.
/// Full 3,988 discrepancies ≈ 5.5 hours across multiple sessions.
/// </summary>
public static class EnrichCandidatesCommand
{
    private const int DelayMs = 0_800;

    public sealed record Options(string Country = "", int Limit = 100, bool DryRun = false, bool All = false);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }
        if (!TryParseArgs(args, out var opts)) { PrintUsage(); return 2; }
        return await RunAsync(opts);
    }

    public static async Task<int> RunAsync(Options opts)
    {
        if (opts.All)
            return await CountryRunner.ForEachWithRuleAsync(cc => RunAsync(opts with { Country = cc, All = false }));

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

        // Resolve the latest run for this country — used only as the audit RunId in
        // pipeline.Decisions (FK requirement). Not shown to the user; enrichment
        // aggregates discrepancies across all runs for the country.
        var auditRunId = await GetLatestRunIdAsync(db, opts.Country);
        if (auditRunId == null)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ No runs found for {opts.Country.ToUpperInvariant()}. " +
                "Import a candidates file first with 'importcandidates'.");
            return 1;
        }

        // Load all unresolved discrepancies for this country across all runs
        var pending = await db.Discrepancies.GetPendingAsync(opts.Country);

        // Filter out special-domain codes before enrichment.
        var rules = CountryRulesFactory.For(opts.Country);
        var specialCodeSet = pending
            .Where(d => rules.IsEnrichmentSkipped(d.ZpCode) || rules.IsKnownSpecialName(d.PlaceName))
            .Select(d => d.ZpCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Get distinct enrichable zips only
        var zips = pending
            .Select(d => d.ZpCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(code => !specialCodeSet.Contains(code))
            .ToList();

        EnrichCandidatesDisplay.PrintHeader(
            opts.Country,
            zips.Count + specialCodeSet.Count, specialCodeSet.Count,
            zips.Count, opts.Limit, DelayMs / 1000, opts.DryRun);
        Console.WriteLine();

        if (zips.Count == 0)
        {
            Console.WriteLine("  No unresolved discrepancies — nothing to enrich.");
            return 0;
        }

        var batch = zips.Take(opts.Limit).ToList();

        if (opts.DryRun)
        {
            foreach (var z in batch)
                AnsiConsole.MarkupLine($"  [grey][[dry-run]] Would enrich zip {Markup.Escape(z)}[/]");
            return 0;
        }

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ZipPostLookup.CountryDataTools/1.0 (candidate enrichment)");

        await using var conn = (SqlConnection)db.GetFactory().CreateConnection();

        var counters = new EnrichCandidateCounters
        {
            SpecialCodes = specialCodeSet.Count
        };

        var unfoundCount = 0;
        string? unfoundLogPath = null;
        try
        {
            var logDir = Path.Combine(db.RepoRoot, "Logs");
            Directory.CreateDirectory(logDir);
            unfoundLogPath = Path.Combine(logDir,
                $"enrich-unfound_{opts.Country.ToUpperInvariant()}_{DateTimeOffset.UtcNow:yyyyMMdd}.log");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ Could not prepare unfound log directory: {ex.Message}");
        }

        // Build the round-robin router once for the entire batch
        var apiKeys = ApiKeysConfig.TryLoad(db.RepoRoot);
        var apis = EnrichmentApiFactory.GetApisForCountry(opts.Country, http, apiKeys);
        var router = new RoundRobinEnrichmentRouter(apis);

        var periodUsage = await ApiUsageRepository.GetCurrentPeriodUsageAsync(conn);
        foreach (var (name, count) in periodUsage)
            router.SeedCallCount(name, count);

        foreach (var api in apis)
            counters.InitDailyUsage(api.Name,
                periodUsage.GetValueOrDefault(api.Name, 0),
                api.DailyLimit ?? api.MonthlyLimit);

        var userCancelled = await EnrichmentEngine.RunAsync(new EnrichmentRun
        {
            Conn          = conn,
            Country       = opts.Country,
            Rules         = rules,
            Batch         = batch,
            Counters      = counters,
            Router        = router,
            Apis          = apis,
            DelayMs       = DelayMs,
            IsDirectMode  = false,
            GetStateAsync = async code => await GetCandidateStateAsync(conn, opts.Country, code),
            PersistAsync  = async (c, tx, items) =>
            {
                foreach (var item in items)
                {
                    if (item.Outcome == FetchOutcome.NotFound)
                        await MarkUnfoundAsync(c, opts.Country, item.Zip, tx);
                    else
                        await ResolveDiscrepanciesAsync(c, opts.Country, auditRunId, item.Zip, item.Result!, item.ApiName!, tx);

                    if (item.Outcome == FetchOutcome.Found)
                    {
                        var newName = await ReferenceEnrichmentHelper.UpdateReferenceAsync(
                            c, opts.Country, item.Zip, item.Result!, tx, rules.ResolveAdmin1(item.Zip));
                        if (newName) counters.NewNamesInserted++;
                    }
                }
            },
            OnLongPlaceNameAsync = async (code, api, name) =>
            {
                if (unfoundLogPath is null) return;
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}  {opts.Country.ToUpperInvariant()}  {code,-10}  unfound (name too long: {name.Length} chars)  via {api,-14}  {name}";
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
        EnrichCandidatesDisplay.PrintSummary(counters);

        var gold = await GoldCertifier.CertifyAsync(conn, opts.Country);
        if (gold.Failed)
            AnsiConsole.MarkupLine($"  [grey](Gold certification skipped: {Markup.Escape(gold.Error!)})[/]");
        else if (gold.Certified > 0)
            AnsiConsole.MarkupLine($"  [bold yellow]⭐ Gold: {gold.Certified:N0} new code(s) certified[/]");

        if (unfoundCount > 0 && unfoundLogPath != null)
        {
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                $"  [grey]{unfoundCount} over-long name(s) logged as unfound to[/] {Markup.Escape(unfoundLogPath)}");
        }

        int remaining = zips.Count - batch.Count;
        if (remaining > 0)
        {
            var etaMinutes = remaining * (DelayMs / 1000.0) / 60;
            Console.WriteLine();
            Console.WriteLine($"  {remaining:N0} zips remaining — " +
                              $"run again to continue (~{etaMinutes:F0} min at {opts.Limit}/session).");
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Resolve all discrepancy field rows for a zip using the API result
    // -------------------------------------------------------------------------

    private static async Task ResolveDiscrepanciesAsync(
        SqlConnection conn,
        string country, string auditRunId, string code,
        ApiLookupResult result, string apiName, SqlTransaction tx)
    {
        // Get all cities for this code with unresolved discrepancies (across all runs)
        var cities = await conn.QueryAsync<string>(
            CommonQueries.GetDistinctNamesFromDiscrepancies,
            new { CountryId = country.ToUpperInvariant(), ZpCode = code },
            transaction: tx);

        var parameters = new DynamicParameters();
        parameters.Add("CountryId", country.ToUpperInvariant());
        parameters.Add("ZpCode", code);
        parameters.Add("OverrideName", result.PlaceName);
        parameters.Add("State", result.Admin1Code);
        parameters.Add("StateName", result.Admin1Name);
        parameters.Add("Timezone", result.Timezone);
        parameters.Add("Status", nameof(CandidateStatus.Clean));

        var decidedBy = result.Timezone != null
            ? $"auto:{apiName.ToLowerInvariant().Replace('.', '-')}+geotimezone"
            : $"auto:{apiName.ToLowerInvariant().Replace('.', '-')}";

        var notes = result.Timezone != null
            ? $"Enriched via {apiName} lat:{result.Lat} lon:{result.Lon} tz:{result.Timezone}"
            : $"Enriched via {apiName} (no timezone)";

        foreach (var name in cities)
        {
            var loopParameters = new DynamicParameters(parameters);
            loopParameters.Add("PlaceName", name);
            await conn.ExecuteAsync(
                CommonQueries.UpdateDiscrepancyWithOverride,
                loopParameters,
                transaction: tx);

            await conn.InsertAsync(new PipelineDecisions
            {
                CountryId = country.ToUpperInvariant(),
                RunId = auditRunId,
                ZpCode = code,
                PlaceName = name,
                AcceptIncoming = true,
                DecidedBy = decidedBy,
                Notes = notes,
                CreatedAt = DateTimeOffset.UtcNow,
            }, tx);

            await conn.ExecuteAsync(
                CommonQueries.UpdateCandidateStatus,
                loopParameters,
                transaction: tx);
        }
    }

    // -------------------------------------------------------------------------
    // Mark candidate as unfound
    // -------------------------------------------------------------------------

    private static async Task MarkUnfoundAsync(
        SqlConnection conn, string country, string zip, SqlTransaction tx)
    {
        await conn.ExecuteAsync(
            CommonQueries.UpdateCandidateStatusUnfound,
            new
            {
                CountryId = country.ToUpperInvariant(),
                ZpCode = zip,
            },
            transaction: tx);

        await conn.ExecuteAsync(
            CommonQueries.MarkDiscrepanciesProcessed,
            new
            {
                CountryId = country.ToUpperInvariant(),
                ZpCode = zip,
            },
            transaction: tx);
    }

    // -------------------------------------------------------------------------
    // Get candidate state for territory routing
    // -------------------------------------------------------------------------

    private static async Task<string> GetCandidateStateAsync(
        SqlConnection conn, string country, string zip)
    {
        var code = await conn.ExecuteScalarAsync<string?>(
            CommonQueries.GetCandidateStateCode,
            new
            {
                CountryId = country.ToUpperInvariant(),
                ZpCode = zip,
            });

        return code ?? "";
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the most recent RunId for the country from pipeline.Runs.
    /// Used only as the audit RunId for pipeline.Decisions inserts — never
    /// shown to the user.
    /// </summary>
    private static async Task<string?> GetLatestRunIdAsync(WorkDbContext db, string country)
    {
        using var conn = db.GetFactory().CreateConnection();
        var latest = await conn.QueryFirstOrDefaultAsync<PipelineRuns>(
            CommonQueries.GetLatestRun,
            new { CountryId = country.ToUpperInvariant() });
        return latest?.RunId;
    }

    private static bool TryParseArgs(string[] args, out Options opts)
    {
        var country = args.OptionValue("--country", rejectFlagValue: true) ?? "";
        opts = new Options(
            Country: country,
            Limit:   args.IntOption("--limit", 100, min: 1),
            DryRun:  args.HasFlag("--dry-run"),
            All:     args.HasFlag("--all"));
        return opts.All || !string.IsNullOrWhiteSpace(country);
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools enrichcandidates --country XX [--limit N] [--dry-run]
                or countrydatatools enrichcandidates --all         [--limit N] [--dry-run]

              --country XX    Country code (US / CA / MX)
              --all           Run for all pipeline countries (US, CA, MX) in sequence
              --limit N       Max zips to enrich this session (default: 100)
              --dry-run       Show what would be processed without making requests
            """);
}
