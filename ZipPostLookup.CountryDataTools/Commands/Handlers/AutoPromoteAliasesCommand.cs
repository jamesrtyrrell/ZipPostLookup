using Dapper;
using Microsoft.Data.SqlClient;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.CountryRules;
using ZipPostLookup.CountryDataTools.Database;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Validation;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools autopromote --country US|CA|MX [--dry-run]
///                               --all              [--dry-run]
///
/// Phase 3 of the Gold Name-Discrepancy Backlog Resolution. Auto-promotes candidate
/// aliases by comparing unresolved Name discrepancy InValues against existing
/// PlaceNames for each code using PlaceNameNormalizer. When a match is found
/// (abbreviation/spelling-equivalent), inserts an AltNameOf row and resolves the
/// discrepancies.
///
/// Algorithm per country:
///   1. Query pipeline.OpenNameDiscrepancies for non-blank, not-already-a-row candidates
///   2. For each (ZpCode, InValue): fetch all existing PlaceNames for that code
///   3. Use PlaceNameNormalizer.AreEquivalent with country-specific languages
///   4. On match: insert data.Reference row with AltNameOf = matched canonical name
///   5. Resolve all matching discrepancies (AcceptIncoming=1, Process=1, ResolvedAt=now)
///
/// Conservative: only promotes when PlaceNameNormalizer judges names equivalent.
/// Leftovers remain for manual review (Phase 4).
/// </summary>
public static class AutoPromoteAliasesCommand
{
    public sealed record Options(string Country = "", bool DryRun = false, bool All = false);

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

        var countryId = opts.Country.ToUpperInvariant();
        var rules = CountryRulesFactory.For(opts.Country);
        var languages = rules.PlaceNameLanguages;

        await using var conn = (SqlConnection)db.GetFactory().CreateConnection();

        // Fetch candidate aliases from the deduplicated view
        var candidates = (await conn.QueryAsync<CandidateAlias>(
            CommonQueries.GetCandidateAliasesForCountry,
            new { CountryId = countryId })).ToList();

        PrintHeader(countryId, candidates.Count, opts.DryRun);

        if (candidates.Count == 0)
        {
            Console.WriteLine("  No candidate aliases — nothing to promote.");
            return 0;
        }

        var promoted = 0;
        var skipped = 0;

        foreach (var candidate in candidates)
        {
            // Get all existing PlaceNames for this code
            var existingNames = (await conn.QueryAsync<string>(
                CommonQueries.GetExistingPlaceNamesForCode,
                new { CountryId = countryId, candidate.ZpCode })).ToList();

            if (existingNames.Count == 0)
            {
                // Should not happen — OpenNameDiscrepancies view guarantees a RefValue
                skipped++;
                continue;
            }

            // Find the first existing name that matches the candidate via PlaceNameNormalizer
            var matchedName = existingNames.FirstOrDefault(existingName =>
                PlaceNameNormalizer.AreEquivalent(candidate.InValue, existingName, languages));

            if (matchedName == null)
            {
                // No abbreviation/spelling-equivalent match — leave for Phase 4 manual review
                skipped++;
                continue;
            }

            if (opts.DryRun)
            {
                AnsiConsole.MarkupLine(
                    $"  [grey][[dry-run]] Would promote {Markup.Escape(candidate.ZpCode),-12}  " +
                    $"{Markup.Escape(candidate.InValue),-30} → alias of {Markup.Escape(matchedName)}[/]");
                promoted++;
                continue;
            }

            // Insert the AltNameOf row
            var aliasRow = new DataReference
            {
                CountryId = countryId,
                ZpCode = candidate.ZpCode,
                PlaceName = candidate.InValue,
                Timezone = "---",
                Lat = "---",
                Lng = "---",
                IsDefault = false,
                TimezoneChecked = false,
                NameChecked = false,
                AltNameOf = matchedName
            };

            await db.Data.MergeDataRecordsAsync(new List<IDataSchema> { aliasRow });

            // Resolve all matching discrepancies for this (ZpCode, InValue)
            await db.Exec.ExecuteAsync(
                CommonQueries.ResolveDiscrepanciesForPromotedAlias,
                new { CountryId = countryId, candidate.ZpCode, InValue = candidate.InValue });

            AnsiConsole.MarkupLine(
                $"  [green]✓[/] {Markup.Escape(candidate.ZpCode),-12}  " +
                $"{Markup.Escape(candidate.InValue),-30} → alias of {Markup.Escape(matchedName)}");
            promoted++;
        }

        Console.WriteLine();

        // Close any discrepancies whose InValue is already a Reference row (may have been added
        // via editor or a previous run that didn't close all matching rows).
        if (!opts.DryRun)
        {
            var closed = await db.Exec.ExecuteAsync(
                CommonQueries.CloseAlreadyPromotedDiscrepancies,
                new { CountryId = countryId });
            if (closed > 0)
                AnsiConsole.MarkupLine(
                    $"  [grey]Closed {closed:N0} stale discrepancy row(s) where InValue already exists as a Reference row.[/]");
        }

        PrintSummary(promoted, skipped, opts.DryRun);

        return 0;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void PrintHeader(string countryId, int candidateCount, bool dryRun)
    {
        var dryRunLabel = dryRun ? " [grey](dry-run)[/]" : "";
        AnsiConsole.MarkupLine($"[bold]Auto-Promote Candidate Aliases{dryRunLabel}[/]");
        AnsiConsole.MarkupLine($"  Country:    [cyan]{countryId}[/]");
        AnsiConsole.MarkupLine($"  Candidates: [yellow]{candidateCount:N0}[/]");
        Console.WriteLine();
    }

    private static void PrintSummary(int promoted, int skipped, bool dryRun)
    {
        var action = dryRun ? "Would promote" : "Promoted";
        AnsiConsole.MarkupLine($"  {action}:  [green]{promoted:N0}[/]");
        AnsiConsole.MarkupLine($"  Skipped:   [grey]{skipped:N0}[/]  (no equivalent match)");
    }

    private static bool TryParseArgs(string[] args, out Options opts)
    {
        opts = new Options();
        var country = "";
        var dryRun = false;
        var all = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--country" when i + 1 < args.Length:
                    country = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--all":
                    all = true;
                    break;
                default:
                    return false;
            }
        }

        if (!all && string.IsNullOrWhiteSpace(country))
            return false;

        opts = new Options(country, dryRun, all);
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: autopromote --country <CC> [--dry-run]
                               --all          [--dry-run]

            Auto-promotes candidate aliases from the Gold Name-Discrepancy backlog
            using PlaceNameNormalizer equivalence matching (abbreviation/spelling).

            Examples:
              autopromote --country US
              autopromote --country CA --dry-run
              autopromote --all

            Options:
              --country <CC>   Two-letter country code (US, CA, MX)
              --all            Run for all countries with rules
              --dry-run        Show what would be promoted without writing to DB

            See PROJECTS.md "Gold Name-Discrepancy Backlog Resolution" Phase 3.
            """);
    }

    // ── DTOs ───────────────────────────────────────────────────────────────────

    private sealed record CandidateAlias
    {
        public string ZpCode { get; init; } = string.Empty;
        public string InValue { get; init; } = string.Empty;
        public string RefValue { get; init; } = string.Empty;
    }
}
