using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Validation;

/// <summary>
/// Country-level domain rules for CountryDataTools pipeline commands.
/// Answers questions about special code ranges and name patterns that require
/// different handling at import, enrichment, and integrity-check time.
///
/// Distinct from <c>ZipPostLookup.Normalizers.ICountryCodeRules</c> (format/normalise)
/// in the library — this interface is about domain knowledge, not string formatting.
/// </summary>
public interface ICountryRules
{
    PipelineCountry Country { get; }

    /// <summary>
    /// True if <paramref name="code"/> belongs to a known special domain
    /// (military, territory, PRS) that should not be treated as a new code
    /// during import or as a candidate for external enrichment.
    /// </summary>
    bool IsKnownSpecialCode(string code);

    /// <summary>
    /// True if <paramref name="name"/> identifies a known special domain
    /// regardless of the code value. Catches rows where the code is
    /// ambiguous but the name is unambiguous — e.g. "APO AA", "FPO AE",
    /// or the simple reference forms "Apo", "Fpo", "Dpo".
    /// Either <see cref="IsKnownSpecialCode"/> or <see cref="IsKnownSpecialName"/>
    /// returning true is sufficient to treat a row as special.
    /// </summary>
    bool IsKnownSpecialName(string name);

    /// <summary>
    /// Human-readable label for the domain that owns <paramref name="code"/>,
    /// e.g. "APO/FPO/DPO (Armed Forces Europe)". Returns null if the code is
    /// not in a recognised special domain.
    /// </summary>
    string? GetDomainLabel(string code);

    /// <summary>
    /// True if external enrichment APIs (e.g. Zippopotam.us) are expected to
    /// return a non-result for this code. Commands skip API calls for these
    /// codes and count them as skipped rather than unfound.
    /// </summary>
    bool IsEnrichmentSkipped(string code);

    /// <summary>
    /// True if geographic coordinate resolution should be skipped for this code.
    /// Military APO/FPO codes have no fixed lat/lng; including them in
    /// coordinate-dependent integrity checks produces false failures.
    /// </summary>
    bool IsCoordResolutionSkipped(string code);

    /// <summary>
    /// Returns the code to send to external enrichment APIs (e.g. Zippopotam.us).
    /// Most countries use the full code. Canada truncates to the 3-char FSA because
    /// Zippopotam.us only has FSA-level data — full LDU codes (e.g. M5V3L9) return
    /// 404 or empty JSON, while the FSA prefix (M5V) resolves correctly.
    /// Default implementation returns the code unchanged.
    /// </summary>
    string GetApiLookupCode(string code) => code;

    /// <summary>
    /// Languages used by <see cref="PlaceNameNormalizer"/> when expanding
    /// abbreviations to detect equivalent place names (e.g. "St. Martin" /
    /// "Saint Martin"). Language names must match keys in LanguageAbbreviations.json.
    /// Default: English only.
    /// </summary>
    IReadOnlyList<string> PlaceNameLanguages => ["English"];
}
