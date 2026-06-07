using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Validation.Ca;

/// <summary>
/// CA-specific domain rules. No special postal-code domains (military, PRS etc.)
/// exist for Canada; all methods return false / null. Extend here as needed.
/// </summary>
public sealed class CaCountryRules : ICountryRules
{
    public PipelineCountry Country                       => PipelineCountry.CA;
    public bool IsKnownSpecialCode(string code)          => false;
    public bool IsKnownSpecialName(string name)          => false;
    public string? GetDomainLabel(string code)           => null;
    public bool IsEnrichmentSkipped(string code)         => false;
    public bool IsCoordResolutionSkipped(string code)    => false;

    /// Zippopotam.us only carries FSA-level data for Canada.
    /// Full LDU codes (e.g. M5V3L9) 404; the 3-char FSA prefix (M5V) resolves correctly.
    public string GetApiLookupCode(string code) =>
        code.Length >= 3 ? code[..3] : code;

    public IReadOnlyList<string> PlaceNameLanguages => ["English", "French"];

    // ── Province/territory name → ISO 3166-2:CA code ──────────────────────────
    // Covers the English names returned by the GeoLocator API.

    private static readonly Dictionary<string, string> _provinceCode =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Alberta"]                   = "AB",
            ["British Columbia"]          = "BC",
            ["Manitoba"]                  = "MB",
            ["New Brunswick"]             = "NB",
            ["Newfoundland and Labrador"] = "NL",
            ["Northwest Territories"]     = "NT",
            ["Nova Scotia"]               = "NS",
            ["Nunavut"]                   = "NU",
            ["Ontario"]                   = "ON",
            ["Prince Edward Island"]      = "PE",
            ["Quebec"]                    = "QC",
            ["Québec"]                    = "QC",
            ["Saskatchewan"]              = "SK",
            ["Yukon"]                     = "YT",
        };

    public string? ResolveAdmin1CodeFromName(string admin1Name) =>
        _provinceCode.TryGetValue(admin1Name, out var code) ? code : null;
}
