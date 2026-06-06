using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Validation.Mx;

/// <summary>
/// MX-specific domain rules. No special postal-code domains exist for Mexico;
/// all methods return false / null. Extend here as needed.
/// </summary>
public sealed class MxCountryRules : ICountryRules
{
    public PipelineCountry Country                       => PipelineCountry.MX;
    public bool IsKnownSpecialCode(string code)          => false;
    public bool IsKnownSpecialName(string name)          => false;
    public string? GetDomainLabel(string code)           => null;
    public bool IsEnrichmentSkipped(string code)         => false;
    public bool IsCoordResolutionSkipped(string code)    => false;

    public IReadOnlyList<string> PlaceNameLanguages => ["Spanish"];
}
