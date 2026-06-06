using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Validation.Us;

/// <summary>
/// US-specific domain rules for the CountryDataTools pipeline.
///
/// Special code ranges (verified against data.reference 2026-06-05):
///   09000–09999  APO/FPO/DPO AE — Armed Forces Europe / Africa / Middle East / Canada
///   34000–34099  APO/FPO/DPO AA — Armed Forces Americas
///   96200–96699  APO/FPO/DPO AP — Armed Forces Pacific
///   00600–00988  Puerto Rico (006xx, 007xx, 009xx) + US Virgin Islands (008xx)
///   96799        American Samoa
///   96900–96970  Guam (96910–96970) + N. Mariana Islands (96950–96952)
///   56999        Parcel Return Service (PRS) — Minneapolis NDC
///
/// Special name patterns (both incoming GeoNames form and stored reference form):
///   "APO AA" / "FPO AA" / "DPO AA"   Armed Forces Americas
///   "APO AE" / "FPO AE" / "DPO AE"   Armed Forces Europe
///   "APO AP" / "FPO AP" / "DPO AP"   Armed Forces Pacific
///   "Apo" / "Fpo" / "Dpo"            Simple title-case forms stored in data.reference
/// </summary>
public sealed class UsCountryRules : ICountryRules
{
    public PipelineCountry Country => PipelineCountry.US;

    // ── Name patterns ──────────────────────────────────────────────────────────

    private static readonly HashSet<string> _specialNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Full clarifier forms (GeoNames and other external sources)
            "APO AA", "FPO AA", "DPO AA",
            "APO AE", "FPO AE", "DPO AE",
            "APO AP", "FPO AP", "DPO AP",
            // Simple title-case forms stored in data.reference
            "Apo", "Fpo", "Dpo",
        };

    public bool IsKnownSpecialName(string name) =>
        !string.IsNullOrWhiteSpace(name) && _specialNames.Contains(name.Trim());

    // ── Code ranges ────────────────────────────────────────────────────────────

    public bool IsKnownSpecialCode(string code)
    {
        if (string.IsNullOrEmpty(code) || !int.TryParse(code, out var n)) return false;

        return n is
            // Military — Armed Forces Europe/Africa/Middle East (09xxx)
            (>= 9000 and <= 9999)
            // Military — Armed Forces Americas (340xx)
            or (>= 34000 and <= 34099)
            // Military — Armed Forces Pacific (962xx–966xx)
            or (>= 96200 and <= 96699)
            // Territories — Puerto Rico (006xx, 007xx, 009xx) + USVI (008xx)
            or (>= 600 and <= 988)
            // Territory — American Samoa
            or 96799
            // Territories — Guam (96910–96970) + N. Mariana Islands (96950–96952)
            or (>= 96900 and <= 96970)
            // Parcel Return Service
            or 56999;
    }

    public string? GetDomainLabel(string code)
    {
        if (string.IsNullOrEmpty(code) || !int.TryParse(code, out var n)) return null;

        return n switch
        {
            >= 9000  and <= 9999  => "APO/FPO/DPO (Armed Forces Europe)",
            >= 34000 and <= 34099 => "APO/FPO/DPO (Armed Forces Americas)",
            >= 96200 and <= 96699 => "APO/FPO/DPO (Armed Forces Pacific)",
            >= 600   and <= 699   => "Territory — Puerto Rico",
            >= 700   and <= 799   => "Territory — Puerto Rico",
            >= 800   and <= 899   => "Territory — US Virgin Islands",
            >= 900   and <= 988   => "Territory — Puerto Rico",
            96799                 => "Territory — American Samoa",
            >= 96950 and <= 96952 => "Territory — N. Mariana Islands",
            >= 96900 and <= 96970 => "Territory — Guam",
            56999                 => "Parcel Return Service",
            _                     => null,
        };
    }

    /// <summary>
    /// Military codes (APO/FPO/DPO) and PRS are not resolvable by Zippopotam.us.
    /// Territories (Puerto Rico, USVI, Guam etc.) have real addresses and CAN be enriched.
    /// </summary>
    public bool IsEnrichmentSkipped(string code)
    {
        if (string.IsNullOrEmpty(code) || !int.TryParse(code, out var n)) return false;

        return n is
            (>= 9000  and <= 9999)   // APO/FPO AE
            or (>= 34000 and <= 34099) // APO/FPO AA
            or (>= 96200 and <= 96699) // APO/FPO AP
            or 56999;                  // PRS
    }

    /// <summary>
    /// Military codes have no fixed geographic coordinates — exclude from coord checks.
    /// PRS is a mail-processing facility with a fixed location but no meaningful
    /// postal-coordinate use; also excluded.
    /// </summary>
    public bool IsCoordResolutionSkipped(string code)
    {
        if (string.IsNullOrEmpty(code) || !int.TryParse(code, out var n)) return false;

        return n is
            (>= 9000  and <= 9999)
            or (>= 34000 and <= 34099)
            or (>= 96200 and <= 96699)
            or 56999;
    }
}
