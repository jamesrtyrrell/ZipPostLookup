namespace ZipPostLookup.CountryDataTools.Models.Enums;

/// <summary>
/// The three countries for which CountryDataTools runs its full curation pipeline.
/// Replaces scattered <c>HashSet&lt;string&gt; { "CA", "US", "MX" }</c> checks.
/// </summary>
public enum PipelineCountry
{
    US,
    CA,
    MX,
}
