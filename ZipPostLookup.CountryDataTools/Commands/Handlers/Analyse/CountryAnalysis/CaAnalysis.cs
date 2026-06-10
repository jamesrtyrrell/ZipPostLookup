using ZipPostLookup.CountryDataTools.CountryRules.Ca;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse.CountryAnalysis;

/// <summary>
/// CA-specific analysis. Admin1 (province) is derivable from the first letter of the FSA
/// via <see cref="CaCountryRules.ResolveAdmin1"/>.
/// </summary>
public sealed class CaAnalysis : ICountryAnalysis
{
    public string Id => "CA";

    public IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics)
    {
        var rules = new CaCountryRules();

        return AdminDerivability.Evaluate(
            ctx, metrics,
            code => rules.ResolveAdmin1(code)?.Code,
            mechanism: "the FSA first letter → province map (CaCountryRules.ResolveAdmin1)");
    }
}
