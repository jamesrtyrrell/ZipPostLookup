using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.CountryRules.Ca;

/// <summary>
/// CA-specific domain rules. No special postal-code domains (military, PRS etc.)
/// exist for Canada; all methods return false / null. Extend here as needed.
/// </summary>
public sealed class CaCountryRules : ICountryRules
{
    public PipelineCountry Country                       => PipelineCountry.CA;
    public bool SupportsAdmin1Derivation                 => true;
    public bool IsKnownSpecialCode(string code)          => false;
    public bool IsKnownSpecialName(string name)          => false;
    public string? GetDomainLabel(string code)           => null;
    public bool IsEnrichmentSkipped(string code)         => false;
    public bool IsCoordResolutionSkipped(string code)    => false;

    /// Zippopotam.us only carries FSA-level data for Canada.
    /// Full LDU codes (e.g. M5V3L9) 404; the 3-char FSA prefix (M5V) resolves correctly.
    public string GetApiLookupCode(string code) =>
        code.Length >= 3 ? code[..3] : code;

    /// CA ZpCodes are FSA format: letter-digit-letter (e.g. M5V, A1A).
    public string ZpCodeLikePattern => "[A-Z][0-9][A-Z]%";

    public IReadOnlyList<string> PlaceNameLanguages => ["English", "French"];

    // ── FSA first letter → province/territory ISO 3166-2:CA code ──────────────
    // Canada Post allocates the first letter of every FSA to exactly one province
    // or territory. This makes admin1 100% derivable from the code with 0 true
    // exceptions in the current dataset. NU codes share the X prefix with NT
    // (Canada Post treats them identically for routing).
    // Source: Canada Post FSA allocation table.

    private static readonly IReadOnlyDictionary<char, (string Code, string Name)> _fsaProvince =
        new Dictionary<char, (string Code, string Name)>
        {
            ['A'] = ("NL", "Newfoundland and Labrador"),
            ['B'] = ("NS", "Nova Scotia"),
            ['C'] = ("PE", "Prince Edward Island"),
            ['E'] = ("NB", "New Brunswick"),
            ['G'] = ("QC", "Quebec"),
            ['H'] = ("QC", "Quebec"),
            ['J'] = ("QC", "Quebec"),
            ['K'] = ("ON", "Ontario"),
            ['L'] = ("ON", "Ontario"),
            ['M'] = ("ON", "Ontario"),
            ['N'] = ("ON", "Ontario"),
            ['P'] = ("ON", "Ontario"),
            ['R'] = ("MB", "Manitoba"),
            ['S'] = ("SK", "Saskatchewan"),
            ['T'] = ("AB", "Alberta"),
            ['V'] = ("BC", "British Columbia"),
            ['X'] = ("NT", "Northwest Territories"),
            ['Y'] = ("YT", "Yukon"),
        };

    public (string Code, string Name)? ResolveAdmin1(string zpCode) =>
        zpCode.Length > 0 && _fsaProvince.TryGetValue(char.ToUpperInvariant(zpCode[0]), out var v)
            ? v
            : null;

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
