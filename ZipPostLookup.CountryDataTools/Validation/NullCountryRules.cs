using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Validation;

/// <summary>
/// No-op implementation returned for countries that have no special-domain rules.
/// All checks return false; the country code is set to US as a sentinel and
/// callers should never observe it on this path.
/// </summary>
internal sealed class NullCountryRules : ICountryRules
{
    public static readonly NullCountryRules Instance = new();

    private NullCountryRules() { }

    public PipelineCountry Country          => PipelineCountry.US;
    public bool IsKnownSpecialCode(string code) => false;
    public bool IsKnownSpecialName(string name) => false;
    public string? GetDomainLabel(string code)  => null;
    public bool IsEnrichmentSkipped(string code)    => false;
    public bool IsCoordResolutionSkipped(string code) => false;
}
