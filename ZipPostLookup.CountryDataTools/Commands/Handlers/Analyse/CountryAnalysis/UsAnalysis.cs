using ZipPostLookup.CountryDataTools.CountryRules.Us;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse.CountryAnalysis;

/// <summary>
/// US-specific analysis. Admin1 is derivable from the 3-digit SCF prefix
/// (<see cref="UsCountryRules.ResolveAdmin1"/>), and military/territory codes form
/// non-geographic domains that should be segmented out of geographic compression stats.
/// </summary>
public sealed class UsAnalysis : ICountryAnalysis
{
    public string Id => "US";

    public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
    {
        var rules = new UsCountryRules();

        // Headline: admin1 derivability from the SCF prefix.
        foreach (var f in AdminDerivability.Evaluate(
                     ctx, metrics,
                     code =>
                     {
                         var r = rules.ResolveAdmin1(code);
                         // "NonGeographic" is the fallback for unmapped prefixes — not a real derivation.
                         return r is { } v && v.Code != "NonGeographic" ? v.Code : null;
                     },
                     mechanism: "the 3-digit SCF prefix → state map"))
            yield return f;

        // Special-domain segmentation — military/territory codes.
        var special = ctx.Rows.Count(r => rules.IsKnownSpecialCode(r.ZpCode) || rules.IsKnownSpecialName(r.PlaceName));
        if (special > 0)
        {
            metrics["us.specialDomainRows"] = special.ToString("N0");
            yield return new Finding(
                Id: "C03",
                Category: "Data Structure",
                Title: "Non-geographic domains (military / territory) segmented",
                Severity: "Info",
                Summary:
                    $"{special:N0} rows are APO/FPO/DPO, PRS, or territory codes with no fixed geographic " +
                    $"position. They compress differently from geographic ZIPs and skew prefix-homogeneity " +
                    $"stats; treat them as a separate block.",
                AiPrompt:
                    $"US has {special:N0} non-geographic codes (military APO/FPO/DPO, PRS, territories). " +
                    $"Propose handling them as a distinct segment in the embedded data so the geographic " +
                    $"range compression isn't diluted, and so lookups for these still resolve to the right " +
                    $"AA/AE/AP or territory admin1.");
        }
    }
}
