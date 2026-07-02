using ZipPostLookup.CountryDataTools.CountryRules.Ca;
using ZipPostLookup.CountryDataTools.CountryRules.Mx;
using ZipPostLookup.CountryDataTools.CountryRules.Us;

namespace ZipPostLookup.CountryDataTools.CountryRules;

/// <summary>
/// Returns the <see cref="ICountryRules"/> implementation for the given country code.
/// </summary>
public static class CountryRulesFactory
{
    public static ICountryRules For(string countryCode) =>
        countryCode?.ToUpperInvariant() switch
        {
            "US" => new UsCountryRules(),
            "CA" => new CaCountryRules(),
            "MX" => new MxCountryRules(),
            _    => NullCountryRules.Instance,
        };
}
