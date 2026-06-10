using Spectre.Console;
using Spectre.Console.Rendering;
using ZipPostLookup.CountryDataTools.Models.Counters;

namespace ZipPostLookup.CountryDataTools.Commands.Display;

public static class EnrichCandidatesDisplay
{
    // ── Job header ─────────────────────────────────────────────────────────────

    public static void PrintHeader(
        string country, string runId,
        int totalDiscrepancies, int specialSkipped,
        int enrichable, int limit, int delaySeconds, bool dryRun)
    {
        var specialNote = specialSkipped > 0
            ? $"{specialSkipped:N0}  [grey](APO/FPO/territory/PRS — skipped)[/]"
            : "0";

        CommandDisplay.PrintTable($"Enrich Candidates — {country.ToUpperInvariant()}",
            ("Run",                Markup.Escape(runId)),
            ("Total discrepancies", $"{totalDiscrepancies:N0}"),
            ("Special codes",      specialNote),
            ("Enrichable zips",    $"{enrichable:N0}"),
            ("Limit this run",     $"{limit:N0}"),
            ("Delay",              $"{delaySeconds}s per request"),
            ("Dry run",            dryRun ? "[yellow]Yes[/]" : "No"));
    }

    public static void PrintDirectHeader(
        string country,
        int totalUncurated, int specialSkipped, int enrichable,
        int limit, int delaySeconds, bool dryRun)
    {
        var specialNote = specialSkipped > 0
            ? $"{specialSkipped:N0}  [grey](APO/FPO/territory/PRS — skipped)[/]"
            : "0";

        CommandDisplay.PrintTable($"Enrich Direct — {country.ToUpperInvariant()}",
            ("Uncurated codes", $"{totalUncurated:N0}"),
            ("Special codes",   specialNote),
            ("Enrichable",      $"{enrichable:N0}"),
            ("Limit this run",  $"{limit:N0}"),
            ("Delay",           $"{delaySeconds}s per request"),
            ("Dry run",         dryRun ? "[yellow]Yes[/]" : "No"));
    }

    // ── Live progress ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the live renderable shown inside AnsiConsole.Live() — a status line
    /// with a mini progress bar followed by the running counters table.
    /// statusMarkup is pre-computed markup from the handler (where FetchOutcome is visible).
    /// </summary>
    public static IRenderable BuildLiveRenderable(
        int current, int total, string statusMarkup, EnrichCandidateCounters counters,
        TimeSpan elapsed = default, bool isDirectMode = false)
    {
        var pct    = total > 0 ? current * 100 / total : 0;
        var filled = Math.Min(20, pct / 5);
        var bar    = $"[green]{new string('█', filled)}[/][grey]{new string('░', 20 - filled)}[/]";

        var statusLine = new Markup(
            $"  [bold][[{current}/{total}]][/]  {statusMarkup}   [grey]{pct}%[/]  {bar}");

        // ETA line is ALWAYS included (empty string until data is available) so the
        // Rows renderable has a stable height from frame 1. If the height changes
        // mid-run, Spectre's cursor-up count becomes wrong and old lines leak through.
        Markup etaLine;
        if (current > 0 && elapsed.TotalSeconds > 3)
        {
            var rate      = current / elapsed.TotalSeconds;
            var remaining = total - current;
            var etaSpan   = TimeSpan.FromSeconds(remaining / rate);

            static string Fmt(TimeSpan t) => t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes:D2}m"
                : t.TotalMinutes >= 1
                ? $"{(int)t.TotalMinutes}m {t.Seconds:D2}s"
                : $"{t.Seconds}s";

            etaLine = new Markup(
                $"  [grey]Elapsed {Fmt(elapsed)}  ·  ETA ~{Fmt(etaSpan)}  ·  {rate:F1} zip/s[/]");
        }
        else
        {
            etaLine = new Markup("");
        }

        var hintLine = new Markup("  [grey dim]Esc · stop[/]");
        return new Rows(statusLine, new Text(""), BuildCountersTable(counters, isDirectMode), new Text(""), etaLine, hintLine);
    }

    // ── Final summary ─────────────────────────────────────────────────────────

    public static void PrintSummary(EnrichCandidateCounters counters, bool isDirectMode = false) =>
        AnsiConsole.Write(BuildCountersTable(counters, isDirectMode));

    // ── Shared table builder ──────────────────────────────────────────────────

    private static Table BuildCountersTable(EnrichCandidateCounters counters, bool isDirectMode = false)
    {
        var table = new Table()
            .AddColumn("Result")
            .AddColumn(new TableColumn("Count").RightAligned())
            .AddColumn("Description")
            .AddColumn(new TableColumn("Count / Limit").RightAligned())
            .AddColumn(new TableColumn("Transient").RightAligned());

        var resolvedDesc = isDirectMode
            ? "Reference row enriched via {0} — curated"
            : "Discrepancy resolved via {0} — reference updated";
        var resolvedZeroDesc = isDirectMode
            ? "Reference row enriched via API — curated"
            : "Discrepancy resolved via API — reference updated";
        var unfoundDesc = isDirectMode
            ? "Code not found in any API — remains uncurated until next run"
            : "Code not found in API (404) — candidate marked unfound";

        // Use DailyUsage keys (pre-populated for every registered API at run start) so the
        // table always has the same number of rows. ResolvedByApi grows dynamically and
        // would cause Spectre Live to mis-track cursor position, leaking old lines.
        // Non-API resolvers (e.g. "Armed Forces") that appear in ResolvedByApi but not
        // DailyUsage are appended after so they don't affect the stable row count.
        var stableApiNames   = counters.DailyUsage.Keys.ToList();
        var extraResolvers   = counters.ResolvedByApi.Keys
            .Except(stableApiNames, StringComparer.OrdinalIgnoreCase).ToList();
        var allApiNames      = stableApiNames.Concat(extraResolvers).ToList();

        if (allApiNames.Count > 0)
        {
            foreach (var apiName in allApiNames)
            {
                var count     = counters.ResolvedByApi.GetValueOrDefault(apiName, 0);
                var transient = counters.TransientByApi.GetValueOrDefault(apiName, 0);
                table.AddRow(
                    $"Resolved ({apiName})",
                    count > 0 ? $"[green]{count:N0}[/]" : "0",
                    string.Format(resolvedDesc, apiName),
                    DailyUsageMarkup(counters, apiName),
                    transient > 0 ? $"[red]{transient:N0}[/]" : "[grey]0[/]");
            }
        }
        else
        {
            table.AddRow("Resolved", "0", resolvedZeroDesc, "", "");
        }
        table.AddRow("New names inserted", counters.NewNamesInserted > 0 ? $"[cyan]{counters.NewNamesInserted:N0}[/]" : "0", "New place name added to data.reference", "", "");
        table.AddRow("Unfound",            counters.Unfound      > 0 ? $"[yellow]{counters.Unfound:N0}[/]"      : "0", unfoundDesc, "", "");
        table.AddRow("Skipped",            counters.Skipped      > 0 ? $"[yellow]{counters.Skipped:N0}[/]"      : "0", "Transient API error — will retry next run", "", "");
        table.AddRow("Special codes",      $"[grey]{counters.SpecialCodes:N0}[/]",        "[grey]APO/FPO/DPO/territory/PRS — no API call made[/]", "", "");

        return table;
    }

    private static string DailyUsageMarkup(EnrichCandidateCounters counters, string apiName)
    {
        if (!counters.DailyUsage.TryGetValue(apiName, out var usage))
            return "";

        var (today, limit) = usage;
        if (!limit.HasValue)
            return $"[grey]{today:N0} / —[/]";

        // Colour: green < 80%, yellow 80–99%, red >= 100%
        var pct    = (double)today / limit.Value;
        var colour = pct >= 1.0 ? "red" : pct >= 0.8 ? "yellow" : "green";
        return $"[{colour}]{today:N0} / {limit.Value:N0}[/]";
    }
}
