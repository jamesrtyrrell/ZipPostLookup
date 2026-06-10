using Microsoft.Data.SqlClient;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Database.Repositories;
using ZipPostLookup.CountryDataTools.Enrichment;
using ZipPostLookup.CountryDataTools.Enrichment.Api;
using ZipPostLookup.CountryDataTools.Models.Commands;
using ZipPostLookup.CountryDataTools.Utilities;
using ZipPostLookup.CountryDataTools.Validation;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>One enriched (or attempted) code, queued for the next checkpoint flush.</summary>
internal sealed record EnrichCheckpointItem(
    string Zip, ApiLookupResult? Result, string? ApiName, FetchOutcome Outcome);

/// <summary>
/// Parameters for <see cref="EnrichmentEngine.RunAsync"/>. The two callers
/// (`enrich direct` and `enrich candidates`) differ only in <see cref="GetStateAsync"/>
/// (how a code's admin1/state hint is resolved) and <see cref="PersistAsync"/>
/// (how a checkpoint batch is written).
/// </summary>
internal sealed class EnrichmentRun
{
    public required SqlConnection                Conn         { get; init; }
    public required string                       Country      { get; init; }
    public required ICountryRules                Rules        { get; init; }
    public required IReadOnlyList<string>        Batch        { get; init; }
    public required EnrichCandidateCounters      Counters     { get; init; }
    public required RoundRobinEnrichmentRouter   Router       { get; init; }
    public required IReadOnlyList<IEnrichmentApi> Apis        { get; init; }
    public required int                          DelayMs      { get; init; }
    public required bool                         IsDirectMode { get; init; }

    /// <summary>Resolves the admin1/state hint for a code (Armed-Forces detection + router arg).</summary>
    public required Func<string, Task<string?>> GetStateAsync { get; init; }

    /// <summary>Persists one checkpoint batch inside the supplied transaction. Throws to abort.</summary>
    public required Func<SqlConnection, SqlTransaction, IReadOnlyList<EnrichCheckpointItem>, Task> PersistAsync { get; init; }

    /// <summary>
    /// Optional: invoked (code, api, placeName) when a Found result is rejected for an
    /// over-long place name (see <see cref="EnrichmentEngine.MaxPlaceNameLength"/>) and
    /// downgraded to unfound. Lets the caller append it to a run log.
    /// </summary>
    public Func<string, string, string, Task>? OnLongPlaceNameAsync { get; init; }
}

/// <summary>
/// Shared enrichment driver. Owns the entire <see cref="AnsiConsole.Live"/> loop: per-code
/// router lookup, Armed-Forces shortcut, API-usage recording, status line, checkpoint-every-10
/// in a single transaction, ETA, and interruptible (Escape) delay. Extracted from the formerly
/// near-identical bodies of EnrichDirectCommand and EnrichCandidatesCommand.
/// </summary>
internal static class EnrichmentEngine
{
    /// <summary>
    /// Found results whose place name exceeds this length are almost always a mis-parsed /
    /// garbage API response — they are downgraded to unfound (and logged via
    /// <see cref="EnrichmentRun.OnLongPlaceNameAsync"/>) rather than written to the DB.
    /// </summary>
    public const int MaxPlaceNameLength = 195;

    /// <summary>Runs the live enrichment loop. Returns true if the user pressed Escape to stop.</summary>
    public static async Task<bool> RunAsync(EnrichmentRun run)
    {
        var conn     = run.Conn;
        var country  = run.Country;
        var rules    = run.Rules;
        var batch    = run.Batch;
        var counters = run.Counters;
        var router   = run.Router;
        var apis     = run.Apis;
        var direct   = run.IsDirectMode;

        var checkpoint      = new List<EnrichCheckpointItem>();
        var enrichStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Buffer Console.Error for the duration of the Live block. API generic-catch handlers
        // write to stderr on unexpected exceptions; writing inside AnsiConsole.Live corrupts
        // Spectre's cursor tracking. Buffered content is flushed on Dispose, after Live exits.
        using var deferredError = new DeferredConsoleError();

        bool userCancelled = false;

        await AnsiConsole.Live(
            EnrichCandidatesDisplay.BuildLiveRenderable(
                0, batch.Count, "[grey]Starting...[/]", counters, isDirectMode: direct))
            .AutoClear(true)
            .StartAsync(async ctx =>
        {
            for (int i = 0; i < batch.Count; i++)
            {
                var code   = batch[i];
                var isLast = i == batch.Count - 1;

                ctx.UpdateTarget(EnrichCandidatesDisplay.BuildLiveRenderable(
                    i + 1, batch.Count,
                    $"ZIP {Markup.Escape(code),-10}  [grey]fetching...[/]",
                    counters, enrichStopwatch.Elapsed, isDirectMode: direct));

                var state = await run.GetStateAsync(code);

                string statusMarkup;

                // Armed Forces zips have no geographic location — skip API, assign theatre timezone.
                var af = rules.GetArmedForcesEnrichment(state);
                if (af != null)
                {
                    var afResult = new ApiLookupResult { Admin1Code = af.Value.Code, Admin1Name = af.Value.Name, Timezone = af.Value.Timezone };
                    statusMarkup = $"ZIP {Markup.Escape(code),-10}  [grey]Armed Forces ({Markup.Escape(state ?? "")}) → {af.Value.Timezone}[/]";
                    checkpoint.Add(new EnrichCheckpointItem(code, afResult, "Armed Forces", FetchOutcome.Found));
                    counters.IncrementResolved("Armed Forces");
                }
                else
                {
                    var (apiResult, apiName, fetchOutcome) = await router.LookupAsync(
                        country, rules.GetApiLookupCode(code), state);

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

                    // Reject implausibly long place names (>100 chars) — treat as unfound + log.
                    int? rejectedNameLength = null;
                    if (fetchOutcome == FetchOutcome.Found && apiResult is { PlaceName.Length: > MaxPlaceNameLength })
                    {
                        rejectedNameLength = apiResult.PlaceName.Length;
                        if (run.OnLongPlaceNameAsync is not null)
                            await run.OnLongPlaceNameAsync(code, apiName ?? "", apiResult.PlaceName);
                        apiResult    = null;
                        apiName      = null;
                        fetchOutcome = FetchOutcome.NotFound;
                    }

                    var displayName = apiResult?.PlaceName is { Length: > 40 } n
                        ? n[..37] + "..."
                        : apiResult?.PlaceName ?? "";
                    var notFoundText = rejectedNameLength is { } rlen
                        ? $"→ name too long ({rlen} chars) → unfound"
                        : direct ? "→ not found" : "→ 404 not found";
                    statusMarkup = fetchOutcome switch
                    {
                        FetchOutcome.NotFound       => $"ZIP {Markup.Escape(code),-10}  [yellow]{notFoundText}[/]",
                        FetchOutcome.TransientError => $"ZIP {Markup.Escape(code),-10}  [red]→ transient error (all APIs)[/]",
                        FetchOutcome.Found          => $"ZIP {Markup.Escape(code),-10}  [green]→ {Markup.Escape(displayName)}, {Markup.Escape(apiResult!.Admin1Code)}[/]  [grey]{apiResult.Timezone ?? "no tz"}[/]  [grey]via {Markup.Escape(apiName!)}[/]",
                        _                           => $"ZIP {Markup.Escape(code),-10}",
                    };

                    switch (fetchOutcome)
                    {
                        case FetchOutcome.NotFound:
                            checkpoint.Add(new EnrichCheckpointItem(code, null, null, FetchOutcome.NotFound));
                            counters.Unfound++;
                            break;
                        case FetchOutcome.TransientError:
                            counters.Skipped++;
                            break;
                        case FetchOutcome.Found:
                            checkpoint.Add(new EnrichCheckpointItem(code, apiResult, apiName, FetchOutcome.Found));
                            counters.IncrementResolved(apiName!);
                            break;
                    }
                }

                ctx.UpdateTarget(EnrichCandidatesDisplay.BuildLiveRenderable(
                    i + 1, batch.Count, statusMarkup, counters, enrichStopwatch.Elapsed, isDirectMode: direct));

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
                        counters, enrichStopwatch.Elapsed, isDirectMode: direct));

                    string flushStatus;
                    await using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            await run.PersistAsync(conn, tx, checkpoint);
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
                        i + 1, batch.Count, flushStatus, counters, enrichStopwatch.Elapsed, isDirectMode: direct));
                    checkpoint.Clear();
                }

                if (userCancelled) break;

                if (!isLast)
                {
                    // Interruptible delay — poll every 100 ms so Escape is detected promptly.
                    for (int ms = 0; ms < run.DelayMs && !userCancelled; ms += 100)
                    {
                        await Task.Delay(Math.Min(100, run.DelayMs - ms));
                        while (Console.KeyAvailable)
                            if (Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
                                userCancelled = true;
                    }
                }
            }
        });

        return userCancelled;
    }
}
