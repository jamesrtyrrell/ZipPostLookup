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
    // or territory, with one exception: the 'X' block is split between the
    // Northwest Territories and Nunavut. X0A/X0B/X0C are Nunavut; every other X
    // FSA (X0E/X0G/X1A) is NT. The 'X' entry below is the NT default; the NU
    // FSAs are matched ahead of the first-letter lookup in ResolveAdmin1.
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

    // Nunavut shares the 'X' first letter with the Northwest Territories but uses
    // distinct FSAs: X0A/X0B/X0C → NU; every other X FSA (X0E/X0G/X1A) → NT.
    private static readonly HashSet<string> _nunavutFsas =
        new(StringComparer.OrdinalIgnoreCase) { "X0A", "X0B", "X0C" };

    public (string Code, string Name)? ResolveAdmin1(string zpCode)
    {
        if (zpCode.Length == 0) return null;

        var first = char.ToUpperInvariant(zpCode[0]);
        if (first == 'X' && zpCode.Length >= 3 && _nunavutFsas.Contains(zpCode[..3]))
            return ("NU", "Nunavut");

        return _fsaProvince.TryGetValue(first, out var v) ? v : null;
    }

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
