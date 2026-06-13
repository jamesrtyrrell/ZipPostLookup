using Dapper;
using Microsoft.Data.SqlClient;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools find gold --country US [--all]
///
/// Scans <c>data.Reference</c> and certifies every ZpCode that now satisfies all four
/// Gold conditions (curated default row; admin1 on every curated row; timezone on every
/// curated row; coordinates on at least one curated row) into <c>data.GoldCode</c>.
/// Idempotent — already-certified codes are skipped, so it is safe to re-run.
/// </summary>
public static class FindGoldCommand
{
    public sealed record Options(string Country, bool All = false);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        var all     = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));
        var country = args.OptionValue("--country") ?? "";

        if (!all && string.IsNullOrWhiteSpace(country))
        {
            PrintUsage();
            return 2;
        }

        return await RunAsync(new Options(country, all));
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

        var countries = opts.All
            ? new[] { "US", "CA", "MX" }
            : new[] { opts.Country.ToUpperInvariant() };

        await using var conn = (SqlConnection)db.GetFactory().CreateConnection();

        foreach (var cc in countries)
        {
            if (opts.All)
                AnsiConsole.Write(new Rule($"[bold]{cc}[/]").LeftJustified());

            Console.WriteLine($"  Certifying Gold-eligible ZpCodes for {cc}…");

            var gold = await GoldCertifier.CertifyAsync(conn, cc);
            if (gold.Failed)
            {
                AnsiConsole.MarkupLine(
                    $"  [grey](Gold certification skipped: {Markup.Escape(gold.Error!)})[/]");
                continue;
            }

            var total = await conn.ExecuteScalarAsync<int>(
                CommonQueries.GetGoldCodeCount, new { CountryId = cc });

            AnsiConsole.MarkupLine(gold.Certified > 0
                ? $"  [bold yellow]⭐ {gold.Certified:N0} new code(s) certified[/]  [grey]({total:N0} Gold total)[/]"
                : $"  [green]✓ No newly-eligible codes[/]  [grey]({total:N0} Gold total)[/]");
        }

        return 0;
    }

    private static void PrintUsage() =>
        Console.WriteLine("Usage: countrydatatools find gold --country US\n" +
                          "    or countrydatatools find gold --all");
}
