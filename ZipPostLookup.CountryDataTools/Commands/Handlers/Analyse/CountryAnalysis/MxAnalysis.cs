using ZipPostLookup.CountryDataTools.CountryRules.Mx;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse.CountryAnalysis;

/// <summary>
/// MX-specific analysis. Estado (admin1) is a pure function of the 5-digit ZIP range
/// (<see cref="MxCountryRules.ResolveAdmin1"/>), so admin1 is almost entirely derivable.
/// </summary>
public sealed class MxAnalysis : ICountryAnalysis
{
    public string Id => "MX";

    public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
    {
        var rules = new MxCountryRules();

        return AdminDerivability.Evaluate(
            ctx, metrics,
            code => rules.ResolveAdmin1(code)?.Code,
            mechanism: "the 5-digit ZIP → estado range table");
    }
}
