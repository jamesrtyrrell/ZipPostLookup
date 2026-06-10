using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.CountryRules;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse;

/// <summary>
/// Shared input for every analysis pass. Holds the loaded reference rows (curated,
/// non-flagged only) plus the country's <see cref="ICountryRules"/>, and pre-computes
/// aggregates that multiple passes would otherwise re-derive.
///
/// One <see cref="AnalysisContext"/> is built per country and handed to both the
/// general passes and the country-specific <see cref="ICountryAnalysis"/>.
/// </summary>
public sealed class AnalysisContext
{
    public string Cc { get; }
    public string CountryName { get; }
    public IReadOnlyList<DataReference> Rows { get; }
    public ICountryRules Rules { get; }

    /// <summary>
    /// True when the country's rules override <see cref="ICountryRules.ResolveAdmin1"/>
    /// — i.e. admin1 can be derived from the code alone (US SCF prefix, MX estado range).
    /// Detected the same way as <c>CdtDbIntegrityCommand</c> to stay consistent.
    /// </summary>
    public bool RulesDeriveAdmin1 { get; }

    public int TotalRows { get; }
    public int UniqueCodes { get; }
    public int UniqueNames { get; }
    public int UniqueTimezones { get; }
    public int UniqueAdmin1 { get; }
    public int RowsWithCoords { get; }
    public int SingleNameCodes { get; }
    public int MultiNameCodes { get; }

    private readonly List<IGrouping<string, DataReference>> _byCode;

    public AnalysisContext(string cc, string countryName, IReadOnlyList<DataReference> rows, ICountryRules rules)
    {
        Cc          = cc;
        CountryName = countryName;
        Rows        = rows;
        Rules       = rules;

        RulesDeriveAdmin1 = rules.SupportsAdmin1Derivation;

        TotalRows       = rows.Count;
        UniqueCodes     = rows.Select(r => r.ZpCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        UniqueNames     = rows.Select(r => r.PlaceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        UniqueTimezones = rows.Select(r => r.Timezone).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        UniqueAdmin1    = rows.Select(r => r.Admin1Code).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        RowsWithCoords  = rows.Count(r => r.Lat != "---" && !string.IsNullOrEmpty(r.Lat));

        _byCode = rows.GroupBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase).ToList();
        SingleNameCodes = _byCode.Count(g => g.Count() == 1);
        MultiNameCodes  = _byCode.Count(g => g.Count() > 1);
    }

    /// <summary>Rows grouped by full ZpCode (cached).</summary>
    public IReadOnlyList<IGrouping<string, DataReference>> ByCode => _byCode;

    /// <summary>Groups the rows by the first <paramref name="len"/> characters of the code.</summary>
    public List<IGrouping<string, DataReference>> GroupByPrefix(int len) =>
        Rows.GroupBy(
                r => r.ZpCode.Length >= len ? r.ZpCode[..len] : r.ZpCode,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
}
