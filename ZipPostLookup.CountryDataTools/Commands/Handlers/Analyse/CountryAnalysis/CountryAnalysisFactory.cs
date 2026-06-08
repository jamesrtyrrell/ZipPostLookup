namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse.CountryAnalysis;

/// <summary>
/// Resolves the <see cref="ICountryAnalysis"/> for a country code. Unknown countries fall
/// back to <see cref="NullCountryAnalysis"/>, so a newly added country still produces the
/// full general report and simply reports that no country pass exists yet.
/// </summary>
public static class CountryAnalysisFactory
{
    public static ICountryAnalysis For(string countryCode) =>
        countryCode?.ToUpperInvariant() switch
        {
            "US" => new UsAnalysis(),
            "CA" => new CaAnalysis(),
            "MX" => new MxAnalysis(),
            _    => NullCountryAnalysis.Instance,
        };
}
