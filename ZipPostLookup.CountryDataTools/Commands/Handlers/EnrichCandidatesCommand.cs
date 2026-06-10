using Dapper;
using Microsoft.Data.SqlClient;
using Dapper.Contrib.Extensions;
using ZipPostLookup.CountryDataTools.Database;
using ZipPostLookup.CountryDataTools.Database.Repositories;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Models.Commands;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Validation;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Enrichment;
using ZipPostLookup.CountryDataTools.Enrichment.Api;
using ZipPostLookup.CountryDataTools.Utilities;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools enrichcandidates --country US [--run run_20260528_001] [--limit 100] [--dry-run]
///
/// For each unresolved discrepancy in [codes].[discrepancies], queries the enrichment API
/// round-robin (Zippopotam.us, Ziptastic) for the zip code and resolves Name, state,
/// state_name, and timezone authoritatively.
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
///  10. Transient error → leave in place for next run
///
/// Default limit: 100 zips per run. At 2s delay that's ~3 minutes.
/// Full 3,988 discrepancies ≈ 5.5 hours across multiple runs.
/// </summary>
public static class EnrichCandidatesCommand
{
    private const int DelayMs = 0_800;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        if (!TryParseArgs(args, out var country, out var runId,
                          out var limit, out var dryRun, out var all))
        {
            PrintUsage();
            return 2;
        }

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

        if (all)
            // Run ID auto-detected per country; --run ignored in --all mode
            return await CountryRunner.ForEachWithRuleAsync(cc => RunAsync(["--country", cc,
                "--limit", limit.ToString(),
                .. (dryRun ? new[]{ "--dry-run" } : Array.Empty<string>())]));

        // Resolve run ID: --run > activeRunId in workdb.json > latest run in pipeline.Runs.
        // Always validate the candidate ID actually exists for this country — workdb.json
        // can become stale if new runs are created without updating it.
        var candidateRunId = string.IsNullOrWhiteSpace(runId) ? db.ActiveRunId : runId;
        runId = await ResolveRunIdAsync(db, candidateRunId, country);

        if (runId == null)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ No runs found for {country.ToUpperInvariant()}.");
            await Console.Error.WriteLineAsync(
                "  Use 'workdb newrun --source <file>' to create one, or pass --run explicitly.");
            return 1;
        }

        // When the resolved run differs from what was requested, give the user a chance
        // to pick. In --all mode keep it silent (batch run, no interactive prompt).
        if (runId != candidateRunId)
        {
            if (all)
            {
                AnsiConsole.MarkupLine($"  [grey]Auto-selected run: {Markup.Escape(runId)}[/]");
            }
            else
            {
                var picked = await PickRunAsync(db, country, candidateRunId);
                if (picked == null) { Console.WriteLine("  Cancelled."); return 0; }
                runId = picked;
            }
        }

        // Load unresolved discrepancies for this run
        var pending = await db.Discrepancies.GetPendingAsync(runId, country);

        // Filter out special-domain codes before enrichment.
        // Checks both code range AND name — catches rows inserted directly into
        // codes.discrepancies that bypassed the normal import classification.
        var rules = CountryRulesFactory.For(country);
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
            country, runId,
            zips.Count + specialCodeSet.Count, specialCodeSet.Count,
            zips.Count, limit, DelayMs / 1000, dryRun);
        Console.WriteLine();

        if (zips.Count == 0)
        {
            Console.WriteLine("  No unresolved discrepancies — nothing to enrich.");
            return 0;
        }

        var batch = zips.Take(limit).ToList();

        if (dryRun)
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
        // Run-scoped unfound log in <repo>/Logs — records codes downgraded to unfound for an
        // over-long place name (>100 chars), appended as they arrive so the record survives a
        // crash/kill/Escape. The codes themselves stay in place for retry.
        var unfoundCount = 0;
        string? unfoundLogPath = null;
        try
        {
            var logDir = Path.Combine(db.RepoRoot, "Logs");
            Directory.CreateDirectory(logDir);
            unfoundLogPath = Path.Combine(logDir, $"enrich-unfound_{runId}.log");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ Could not prepare unfound log directory: {ex.Message}");
        }

        // Build the round-robin router once for the entire batch
        var apiKeys = ApiKeysConfig.TryLoad(db.RepoRoot);
        var apis = EnrichmentApiFactory.GetApisForCountry(country, http, apiKeys);
        var router = new RoundRobinEnrichmentRouter(apis);

        // Seed router with current-period persisted counts so limits are enforced
        // across sessions (daily: today's count; monthly: this month's total).
        var periodUsage = await ApiUsageRepository.GetCurrentPeriodUsageAsync(conn);
        foreach (var (name, count) in periodUsage)
            router.SeedCallCount(name, count);

        // Initialise per-API period usage in counters for display.
        foreach (var api in apis)
            counters.InitDailyUsage(api.Name,
                periodUsage.GetValueOrDefault(api.Name, 0),
                api.DailyLimit ?? api.MonthlyLimit);

        var userCancelled = await EnrichmentEngine.RunAsync(new EnrichmentRun
        {
            Conn          = conn,
            Country       = country,
            Rules         = rules,
            Batch         = batch,
            Counters      = counters,
            Router        = router,
            Apis          = apis,
            DelayMs       = DelayMs,
            IsDirectMode  = false,
            GetStateAsync = async code => await GetCandidateStateAsync(conn, country, runId, code),
            PersistAsync  = async (c, tx, items) =>
            {
                foreach (var item in items)
                {
                    if (item.Outcome == FetchOutcome.NotFound)
                        await MarkUnfoundAsync(c, country, runId, item.Zip, tx);
                    else
                        await ResolveDiscrepanciesAsync(c, country, runId, item.Zip, item.Result!, item.ApiName!, tx);

                    if (item.Outcome == FetchOutcome.Found)
                    {
                        var newName = await ReferenceEnrichmentHelper.UpdateReferenceAsync(
                            c, country, item.Zip, item.Result!, tx, rules.ResolveAdmin1(item.Zip));
                        if (newName) counters.NewNamesInserted++;
                    }
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
        EnrichCandidatesDisplay.PrintSummary(counters);

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

        int remaining = zips.Count - batch.Count;
        if (remaining > 0)
        {
            var etaMinutes = remaining * (DelayMs / 1000.0) / 60;
            Console.WriteLine();
            Console.WriteLine($"  {remaining:N0} zips remaining — " +
                              $"run again to continue (~{etaMinutes:F0} min at {limit}/run).");
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Resolve all discrepancy field rows for a zip using the API result
    // -------------------------------------------------------------------------

    private static async Task ResolveDiscrepanciesAsync(
        SqlConnection conn,
        string country, string runId, string code,
        ApiLookupResult result, string apiName, SqlTransaction tx)
    {
        // Get all cities for this code in discrepancies
        var cities = await conn.QueryAsync<string>(
            CommonQueries.GetDistinctNamesFromDiscrepancies,
            new { CountryId = country.ToUpperInvariant(), RunId = runId, ZpCode = code },
            transaction: tx);

        var parameters = new DynamicParameters();
        parameters.Add("CountryId", country.ToUpperInvariant());
        parameters.Add("RunId", runId);
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
            // PlaceName = the actual PlaceName value in codes.discrepancies (original candidate)
            // result.PlaceName = the authoritative name from the API (override value)
            await conn.ExecuteAsync(
                CommonQueries.UpdateDiscrepancyWithOverride,
                loopParameters,
                transaction: tx);

            // Record in pipeline.decisions using the PipelineDecisions model
            await conn.InsertAsync(new PipelineDecisions
            {
                CountryId = country.ToUpperInvariant(),
                RunId = runId,
                ZpCode = code,
                PlaceName = name,
                AcceptIncoming = true,
                DecidedBy = decidedBy,
                Notes = notes,
                CreatedAt = DateTimeOffset.UtcNow,
            }, tx);

            // Update candidate status to clean
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
        SqlConnection conn, string country, string runId, string zip, SqlTransaction tx)
    {
        await conn.ExecuteAsync(
            CommonQueries.UpdateCandidateStatusUnfound,
            new
            {
                CountryId = country.ToUpperInvariant(),
                RunId = runId,
                ZpCode = zip,
            },
            transaction: tx);

        await conn.ExecuteAsync(
            CommonQueries.MarkDiscrepanciesProcessed,
            new
            {
                CountryId = country.ToUpperInvariant(),
                RunId = runId,
                ZpCode = zip,
            },
            transaction: tx);
    }

    // -------------------------------------------------------------------------
    // Get candidate state for territory routing
    // -------------------------------------------------------------------------

    private static async Task<string> GetCandidateStateAsync(
        SqlConnection conn, string country, string runId, string zip)
    {
        // Admin level 1 code (e.g. state abbreviation) is now stored in
        // [codes].[candidate]_admins. Used only for Armed Forces territory routing (AA/AE/AP).
        var code = await conn.ExecuteScalarAsync<string?>(
            CommonQueries.GetCandidateStateCode,
            new
            {
                CountryId = country.ToUpperInvariant(),
                RunId = runId,
                ZpCode = zip,
            });

        return code ?? "";
    }

    private static bool TryParseArgs(
        string[] args, out string country, out string runId,
        out int limit, out bool dryRun, out bool all)
    {
        country = args.OptionValue("--country", rejectFlagValue: true) ?? "";
        runId   = args.OptionValue("--run") ?? "";
        limit   = args.IntOption("--limit", 100, min: 1);
        dryRun  = args.HasFlag("--dry-run");
        all     = args.HasFlag("--all");

        return all || !string.IsNullOrWhiteSpace(country);
    }

    // -------------------------------------------------------------------------
    // Run ID resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves a run ID for the given country:
    ///   1. If <paramref name="candidateRunId"/> is non-empty and exists in pipeline.Runs
    ///      for the country, return it unchanged.
    ///   2. Otherwise query pipeline.Runs for the most recent run for the country.
    ///   3. Return null if no runs exist for the country.
    /// This prevents workdb.json's activeRunId from silently pointing at a stale / wrong-country run.
    /// </summary>
    private static async Task<string?> ResolveRunIdAsync(
        WorkDbContext db, string? candidateRunId, string country)
    {
        using var conn = db.GetFactory().CreateConnection();
        var ccUpper = country.ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(candidateRunId))
        {
            var exists = await conn.ExecuteScalarAsync<int>(
                CommonQueries.CheckRunExistsForCountry,
                new { RunId = candidateRunId, CountryId = ccUpper });
            if (exists > 0) return candidateRunId;
        }

        var latest = await conn.QueryFirstOrDefaultAsync<PipelineRuns>(
            CommonQueries.GetLatestRun, new { CountryId = ccUpper });
        return latest?.RunId;
    }

    /// <summary>
    /// Lists all pipeline runs for the country and lets the user pick one interactively.
    /// Returns the selected run ID, or null if the user cancels.
    /// Called when the requested run ID is stale or missing.
    /// </summary>
    private static async Task<string?> PickRunAsync(
        WorkDbContext db, string country, string? missingRunId)
    {
        using var conn = db.GetFactory().CreateConnection();
        var ccUpper = country.ToUpperInvariant();

        var runs = (await conn.QueryAsync<PipelineRuns>(
            CommonQueries.GetAllRuns, new { CountryId = ccUpper })).ToList();

        if (runs.Count == 0)
        {
            await Console.Error.WriteLineAsync($"  ✗ No runs found for {ccUpper}.");
            return null;
        }

        var title = string.IsNullOrWhiteSpace(missingRunId)
            ? $"Select a run for [bold]{ccUpper}[/]:"
            : $"Run '[yellow]{Markup.Escape(missingRunId)}[/]' not found for [bold]{ccUpper}[/]. Select a run:";

        // Build display strings: RunId  [Status]  StartedAt  SourceFile
        // Use [[ ]] (double brackets) for literal brackets — single brackets are Spectre markup tags.
        var choices = runs
            .Select(r => $"{Markup.Escape(r.RunId),-30}  [[{Markup.Escape(r.Status ?? ""),-12}]]  {r.StartedAt:yyyy-MM-dd HH:mm}  {Markup.Escape(r.SourceFilename ?? "")}")
            .ToList();
        var cancelLabel = "[grey]Cancel[/]";
        choices.Add(cancelLabel);

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"  {title}")
                .UseConverter(s => s)
                .AddChoices(choices));

        if (selected == cancelLabel) return null;

        // The RunId is the first whitespace-delimited token
        return selected.Split(' ', 2)[0].Trim();
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools enrichcandidates --country XX [--run ID] [--limit N] [--dry-run]
                or countrydatatools enrichcandidates --all         [--limit N] [--dry-run]

              --country XX    Country code (US / CA / MX)
              --all           Run for all pipeline countries (US, CA, MX) in sequence
              --run ID        Run ID to enrich (defaults to latest run for the country)
              --limit N       Max zips to enrich this run (default: 100)
              --dry-run       Show what would be processed without making requests
            """);

}
