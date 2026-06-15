using Dapper;
using ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse;
using ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse.CountryAnalysis;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.CountryRules;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools analyse --country CA [--output report.md]
///
/// Analyses the curated, non-flagged rows in <c>data.Reference</c> for a country and writes a
/// structured Markdown report to <c>{cc}-analysis-{date}.md</c>. The analysis is split into:
///
///   · General passes (<see cref="GeneralPasses"/>) — country-agnostic: prefix-homogeneity
///     range compression, columnar/dictionary encoding, size estimates, coverage gaps. These
///     run for every country, so a newly added country is analysable immediately.
///   · A country pass (<see cref="ICountryAnalysis"/> via <see cref="CountryAnalysisFactory"/>)
///     — exploits country-specific structure, chiefly deterministic admin1 derivation from the
///     code (US SCF prefix, MX estado range, CA FSA letter) which can let admin1 be dropped
///     from the shipped image entirely.
///
/// Only runs on countries where <c>DataCurated = 1</c> in <c>data.CountryInfo</c>.
/// </summary>
public static class AnalyseCommand
{
    public sealed record Options(string Country = "", string Output = "", bool All = false);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }
        if (!TryParseArgs(args, out var opts)) { PrintUsage(); return 2; }
        return await RunAsync(opts);
    }

    public static async Task<int> RunAsync(Options opts)
    {
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

        if (opts.All)
            return await CountryRunner.ForEachWithRuleAsync(cc =>
            {
                var ccOutput = string.IsNullOrWhiteSpace(opts.Output)
                    ? ""
                    : Path.Combine(opts.Output, $"{cc.ToLowerInvariant()}-analysis-{DateTime.UtcNow:yyyyMMdd}.md");
                return RunForCountryAsync(db, cc, ccOutput);
            });

        return await RunForCountryAsync(db, opts.Country.ToUpperInvariant(), opts.Output);
    }

    private static async Task<int> RunForCountryAsync(WorkDbContext db, string cc, string output)
    {
        using var conn = db.GetFactory().CreateConnection();

        var countryInfo = await conn.QueryFirstAsync<DataCountryInfo>(
            CommonQueries.GetCountryInfoById, new { CountryId = cc });

        if (string.IsNullOrEmpty(countryInfo.CountryName))
        {
            await Console.Error.WriteLineAsync($"  ✗ Country '{cc}' not found in data.CountryInfo.");
            return 1;
        }

        if (!countryInfo.DataCurated)
        {
            await Console.Error.WriteLineAsync(
                $"  ✗ {cc} is not fully curated (DataCurated = 0). Run the curation pipeline first.");
            return 1;
        }

        Console.WriteLine($"Analysing {cc} ({countryInfo.CountryName}) — curated, non-flagged rows only...");
        Console.WriteLine("  Loading reference rows...");

        var rows = await LoadRowsAsync(conn, cc);
        Console.WriteLine($"  Loaded {rows.Count:N0} rows. Running analysis passes...");

        var ctx     = new AnalysisContext(cc, countryInfo.CountryName, rows, CountryRulesFactory.For(cc));
        var metrics = new Dictionary<string, string>(StringComparer.Ordinal);
        var findings = new List<Finding>();

        foreach (var pass in GeneralPasses.All)
            findings.AddRange(SafeRun(pass, ctx, metrics));

        findings.AddRange(SafeRun(CountryAnalysisFactory.For(cc), ctx, metrics));

        var report = new AnalysisReportBuilder(ctx, findings, metrics).Build();

        if (string.IsNullOrWhiteSpace(output))
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "DataAnalysis");
            Directory.CreateDirectory(dir);
            output = Path.Combine(dir, $"{cc.ToLowerInvariant()}-analysis-{DateTime.UtcNow:yyyyMMdd}.md");
        }

        await File.WriteAllTextAsync(output, report);
        Console.WriteLine($"  ✓ Report written to: {output}");
        return 0;
    }

    // ── Data load ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads curated, non-flagged rows with their admin levels. Dedupes by ReferenceId so a
    /// row with multiple admin levels appears once with all its admins attached (the raw
    /// LEFT JOIN returns one DB row per admin).
    /// </summary>
    private static async Task<List<DataReference>> LoadRowsAsync(System.Data.IDbConnection conn, string cc)
    {
        var map = new Dictionary<long, DataReference>();

        await conn.QueryAsync<DataReference, DataReferenceAdmin, DataReference>(
            CommonQueries.GetAllReferenceDataWithAdmins,
            (r, a) =>
            {
                if (!map.TryGetValue(r.ReferenceId, out var existing))
                {
                    existing = r;
                    map[r.ReferenceId] = existing;
                }
                if (a is not null) existing.AdminReferenceList.Add(a);
                return existing;
            },
            new { CountryId = cc },
            splitOn: "ReferenceAdminId");

        // Sync Admin1 / Admin1Code flat properties from the AdminReferenceList that the
        // multi-map callback populated. The Dapper default constructor leaves them at "---";
        // the level-1 sync only runs in the CodeEntry constructor path, not here.
        var result = map.Values.ToList();
        foreach (var r in result)
        {
            var level1 = r.AdminReferenceList.OrderBy(a => a.AdminLevelId).FirstOrDefault();
            if (level1 != null)
            {
                r.Admin1     = level1.Value;
                r.Admin1Code = level1.Code;
            }
        }
        return result;
    }

    // ── Pass execution — a failing pass must not abort the whole report ──────────

    private static IEnumerable<Finding> SafeRun(
        IAnalysisPass pass, AnalysisContext ctx, IDictionary<string, string> metrics)
    {
        try
        {
            return pass.Run(ctx, metrics).ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  ⚠ Pass {pass.Id} failed: {ex.Message}");
            return
            [
                new Finding(
                    Id: pass.Id,
                    Category: "Error",
                    Title: $"Pass {pass.Id} failed",
                    Severity: "Info",
                    Summary: $"This pass threw and was skipped: {ex.Message}",
                    AiPrompt: ""),
            ];
        }
    }

    // ── Arg parsing ─────────────────────────────────────────────────────────────

    private static bool TryParseArgs(string[] args, out Options opts)
    {
        var country = args.OptionValue("--country", rejectFlagValue: true) ?? "";
        opts = new Options(
            Country: country,
            Output:  args.OptionValue("--output") ?? "",
            All:     args.HasFlag("--all"));
        return opts.All || !string.IsNullOrWhiteSpace(country);
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools analyse --country CA [--output report.md]
                or countrydatatools analyse --all        [--output dir/]

              --country XX   Country code (US / CA / MX) — must be fully curated
              --all          Run for all pipeline countries (US, CA, MX) in sequence
              --output       Report path (single) or directory (--all); default: DataAnalysis/
            """);
}
