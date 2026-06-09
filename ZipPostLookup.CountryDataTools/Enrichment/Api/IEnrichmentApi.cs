namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

internal interface IEnrichmentApi
{
    string Name { get; }
    IReadOnlySet<string> SupportedCountries { get; }

    /// <summary>
    /// Maximum calls allowed per day on the current plan, or null if no daily cap.
    /// </summary>
    int? DailyLimit { get; }

    /// <summary>
    /// Maximum calls allowed per calendar month, or null if no monthly cap.
    /// Mutually exclusive with DailyLimit — set one or the other.
    /// The router uses DailyLimit ?? MonthlyLimit as the effective limit.
    /// </summary>
    int? MonthlyLimit { get; }

    Task<FetchResult> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default);
}
