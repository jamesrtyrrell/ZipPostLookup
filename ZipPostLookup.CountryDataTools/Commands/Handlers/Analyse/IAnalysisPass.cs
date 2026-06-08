namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse;

/// <summary>
/// One analysis pass over a country's dataset. Passes emit <see cref="Finding"/>s and may
/// write numeric results into the shared <c>metrics</c> bag for the report's summary tables.
/// Passes must not mutate the rows.
/// </summary>
public interface IAnalysisPass
{
    string Id { get; }

    IEnumerable<Finding> Run(AnalysisContext ctx, IDictionary<string, string> metrics);
}

/// <summary>
/// A country-specific pass. Resolved by <see cref="CountryAnalysis.CountryAnalysisFactory"/> so
/// that adding a new country with no bespoke analysis still produces the full general report
/// (via <see cref="CountryAnalysis.NullCountryAnalysis"/>), and country passes are added only
/// where the country's rules expose something worth exploiting (deterministic admin derivation,
/// special-domain segmentation).
/// </summary>
public interface ICountryAnalysis : IAnalysisPass;
