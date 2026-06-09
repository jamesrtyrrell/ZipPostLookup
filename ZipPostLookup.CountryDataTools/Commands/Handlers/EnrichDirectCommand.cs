using Dapper;
using Microsoft.Data.SqlClient;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Display;
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
    private const int DelayMs = 1_500;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        if (!TryParseArgs(args, out var country, out var limit, out var dryRun, out var all))
        {
            PrintUsage();
            return 2;
        }

        if (all)
        {
            var exitCode = 0;
            foreach (var cc in new[] { "US", "CA", "MX" })
            {
                Console.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]{cc}[/]").LeftJustified());
                var result = await RunAsync(["--country", cc,
                    "--limit", limit.ToString(),
                    .. (dryRun ? new[] { "--dry-run" } : Array.Empty<string>())]);
                if (result != 0) exitCode = result;
            }
            return exitCode;
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
        var checkpoint = new List<(string Zip, ApiLookupResult? Result, FetchOutcome Outcome)>();
        var enrichStopwatch = System.Diagnostics.Stopwatch.StartNew();

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

        // Buffer Console.Error for the duration of the Live block. API generic-catch handlers
        // write to stderr on unexpected exceptions (401s, quota messages, null JSON fields, etc.).
        // Writing to stderr inside AnsiConsole.Live corrupts Spectre's cursor tracking and causes
        // old status lines to leak through. Buffered content is flushed to real stderr on Dispose,
        // which happens after Live exits — so the messages still appear, just at a safe moment.
        using var deferredError = new DeferredConsoleError();

        bool userCancelled = false;

        await AnsiConsole.Live(
            EnrichCandidatesDisplay.BuildLiveRenderable(
                0, batch.Count, "[grey]Starting...[/]", counters, isDirectMode: true))
            .AutoClear(true)
            .StartAsync(async ctx =>
        {
            for (int i = 0; i < batch.Count; i++)
            {
                var (code, admin1Code) = batch[i];
                var isLast = i == batch.Count - 1;

                ctx.UpdateTarget(EnrichCandidatesDisplay.BuildLiveRenderable(
                    i + 1, batch.Count,
                    $"ZIP {Markup.Escape(code),-10}  [grey]fetching...[/]",
                    counters, enrichStopwatch.Elapsed, isDirectMode: true));

                string statusMarkup;

                var af = rules.GetArmedForcesEnrichment(admin1Code);
                if (af != null)
                {
                    var afResult = new ApiLookupResult { Admin1Code = af.Value.Code, Admin1Name = af.Value.Name, Timezone = af.Value.Timezone };
                    statusMarkup = $"ZIP {Markup.Escape(code),-10}  [grey]Armed Forces ({Markup.Escape(admin1Code)}) → {af.Value.Timezone}[/]";
                    checkpoint.Add((code, afResult, FetchOutcome.Found));
                    counters.IncrementResolved("Armed Forces");
                }
                else
                {
                    var (apiResult, apiName, fetchOutcome) = await router.LookupAsync(
                        country, rules.GetApiLookupCode(code), admin1Code);

                    // Record every API called for this code (router may have tried several
                    // on TransientError before finding a result or exhausting all options).
                    foreach (var (calledName, calledOutcome, _) in router.LastCallLog)
                    {
                        var calledApi = apis.FirstOrDefault(a => a.Name == calledName);
                        await ApiUsageRepository.RecordCallAsync(
                            conn, calledName, calledApi?.DailyLimit, calledApi?.MonthlyLimit);
                        counters.IncrementDailyUsage(calledName);
                        if (calledOutcome == FetchOutcome.TransientError)
                            counters.IncrementTransient(calledName);
                    }

                    statusMarkup = fetchOutcome switch
                    {
                        FetchOutcome.NotFound       => $"ZIP {Markup.Escape(code),-10}  [yellow]→ not found[/]",
                        FetchOutcome.TransientError => $"ZIP {Markup.Escape(code),-10}  [red]→ transient error (all APIs)[/]",
                        FetchOutcome.Found          => $"ZIP {Markup.Escape(code),-10}  [green]→ {Markup.Escape(apiResult!.PlaceName)}, {apiResult.Admin1Code}[/]  [grey]{apiResult.Timezone ?? "no tz"}[/]  [grey]via {Markup.Escape(apiName!)}[/]",
                        _                           => $"ZIP {Markup.Escape(code),-10}",
                    };

                    switch (fetchOutcome)
                    {
                        case FetchOutcome.NotFound:
                            checkpoint.Add((code, null, FetchOutcome.NotFound));
                            counters.Unfound++;
                            break;
                        case FetchOutcome.TransientError:
                            counters.Skipped++;
                            // Per-API transient counts recorded in LastCallLog loop above.
                            break;
                        case FetchOutcome.Found:
                            checkpoint.Add((code, apiResult, FetchOutcome.Found));
                            counters.IncrementResolved(apiName!);
                            break;
                    }
                }

                ctx.UpdateTarget(EnrichCandidatesDisplay.BuildLiveRenderable(
                    i + 1, batch.Count, statusMarkup, counters, enrichStopwatch.Elapsed, isDirectMode: true));

                // Drain any keys pressed during the API call — catch Escape for graceful stop.
                while (Console.KeyAvailable)
                    if (Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
                        userCancelled = true;

                // Flush every 10 actionable results, on the last item, or when user stops.
                if (checkpoint.Count > 0 && (checkpoint.Count % 10 == 0 || isLast || userCancelled))
                {
                    ctx.UpdateTarget(EnrichCandidatesDisplay.BuildLiveRenderable(
                        i + 1, batch.Count,
                        $"[grey]Saving checkpoint ({checkpoint.Count} item(s))...[/]",
                        counters, enrichStopwatch.Elapsed, isDirectMode: true));

                    string flushStatus;
                    await using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var (cpZip, cpResult, cpOutcome) in checkpoint)
                            {
                                if (cpOutcome == FetchOutcome.Found)
                                {
                                    var newName = await ReferenceEnrichmentHelper.UpdateReferenceAsync(
                                        conn, country, cpZip, cpResult!, tx,
                                        rules.ResolveAdmin1(cpZip));
                                    if (newName) counters.NewNamesInserted++;
                                }
                                // NotFound: no DB action — row stays Curated=0, retried next run.
                            }
                            tx.Commit();
                            flushStatus = statusMarkup + "  [grey]✓ saved[/]";
                        }
                        catch (Exception ex)
                        {
                            tx.Rollback();
                            flushStatus = $"[red]⚠ Checkpoint failed: {Markup.Escape(ex.Message)}[/]";
                        }
                    }

                    ctx.UpdateTarget(EnrichCandidatesDisplay.BuildLiveRenderable(
                        i + 1, batch.Count, flushStatus, counters, enrichStopwatch.Elapsed, isDirectMode: true));
                    checkpoint.Clear();
                }

                if (userCancelled) break;

                if (!isLast)
                {
                    // Interruptible delay — poll every 100 ms so Escape is detected promptly.
                    for (int ms = 0; ms < DelayMs && !userCancelled; ms += 100)
                    {
                        await Task.Delay(Math.Min(100, DelayMs - ms));
                        while (Console.KeyAvailable)
                            if (Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
                                userCancelled = true;
                    }
                }
            }
        });

        Console.WriteLine();
        if (userCancelled)
            AnsiConsole.MarkupLine("[yellow]  ⚠ Enrichment stopped by user.[/]");
        else
            Console.WriteLine("Enrichment complete:");
        EnrichCandidatesDisplay.PrintSummary(counters, isDirectMode: true);

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
        country = "";
        limit   = 100;
        dryRun  = false;
        all     = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--country" when i + 1 < args.Length && !args[i + 1].StartsWith('-'):
                    country = args[++i]; break;
                case "--all":
                    all = true; break;
                case "--limit" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var n) && n > 0) limit = n; break;
                case "--dry-run":
                    dryRun = true; break;
            }
        }

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
