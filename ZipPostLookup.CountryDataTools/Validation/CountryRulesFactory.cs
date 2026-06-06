using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Validation.Ca;
using ZipPostLookup.CountryDataTools.Validation.Mx;
using ZipPostLookup.CountryDataTools.Validation.Us;

namespace ZipPostLookup.CountryDataTools.Validation;

/// <summary>
/// Returns the <see cref="ICountryRules"/> implementation for the given country.
/// Both a typed (<see cref="PipelineCountry"/>) and string overload are provided
/// so existing commands that pass raw country-code strings don't need refactoring.
/// </summary>
public static class CountryRulesFactory
{
    public static ICountryRules For(PipelineCountry country) => country switch
    {
        PipelineCountry.US => new UsCountryRules(),
        PipelineCountry.CA => new CaCountryRules(),
        PipelineCountry.MX => new MxCountryRules(),
        _                  => NullCountryRules.Instance,
    };

    public static ICountryRules For(string countryCode) =>
        countryCode?.ToUpperInvariant() switch
        {
            "US" => new UsCountryRules(),
            "CA" => new CaCountryRules(),
            "MX" => new MxCountryRules(),
            _    => NullCountryRules.Instance,
        };
}
