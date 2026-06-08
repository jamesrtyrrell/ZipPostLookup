namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse.CountryAnalysis;

/// <summary>
/// No-op country analysis used when a country has no bespoke passes yet. Adding a new
/// country therefore yields the full general report immediately; country-specific passes
/// are written only once the country's rules expose something exploitable.
/// </summary>
public sealed class NullCountryAnalysis : ICountryAnalysis
{
    public static readonly NullCountryAnalysis Instance = new();

    public string Id => "C00";

    public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
    {
        yield return new Finding(
            Id: Id,
            Category: "Coverage",
            Title: "No country-specific analysis registered",
            Severity: "Info",
            Summary:
                $"{ctx.Cc} has no ICountryAnalysis implementation, so only general passes ran. " +
                $"Add a country analysis to exploit code structure (deterministic admin derivation, " +
                $"special-domain segmentation) for deeper compression and enrichment insight.",
            AiPrompt:
                $"I am adding {ctx.Cc} to ZipPostLookup. Given its postal-code structure, propose an " +
                $"ICountryAnalysis: can admin1 be derived from a code prefix or numeric range (like US " +
                $"SCF prefixes or MX estado ranges)? Are there special non-geographic code domains " +
                $"(military, PO-box, territories) that should be segmented out of compression stats?");
    }
}
