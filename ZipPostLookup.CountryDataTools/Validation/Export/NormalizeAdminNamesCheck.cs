using System.Data;
using Dapper;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Validation.Export;

/// <summary>
/// Pre-export check: detects admin level 1 codes that have multiple distinct
/// name spellings for the same country and normalises all minority variants to
/// the dominant (highest-count) spelling before the CSV is written.
/// </summary>
public sealed class NormalizeAdminNamesCheck : IExportPreCheck
{
    public string Name => "Normalise admin name variants";

    public async Task<int> RunAsync(IDbConnection conn, string ccUpper, string repoRoot)
    {
        var variants = (await conn.QueryAsync<AdminNameVariant>(
            CommonQueries.DetectAdminNameVariants, new { CountryId = ccUpper })).ToList();

        if (variants.Count == 0) return 0;

        foreach (var v in variants)
            AnsiConsole.MarkupLine(
                $"    [yellow]{Markup.Escape(v.Code)}: '{Markup.Escape(v.MinorityValue)}' ({v.MinorityCnt}) → '{Markup.Escape(v.DominantValue)}'[/]");

        return await conn.ExecuteAsync(
            CommonQueries.NormalizeAdminNames, new { CountryId = ccUpper });
    }
}
