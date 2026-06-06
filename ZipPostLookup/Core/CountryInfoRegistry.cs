namespace ZipPostLookup.Core;

/// <summary>
/// Public facade over the embedded <c>countries.json</c> data.
///
/// <see cref="CountryInfoSource"/> is intentionally <c>internal</c> — this class
/// exposes the subset of its functionality needed by external callers such as
/// <c>ZipPostLookup.CountryDataTools</c>.
///
/// <see cref="ZipPostRegistry"/> also calls this to attach a <see cref="CountryInfo"/>
/// to each registry instance, with <see cref="CountryInfo.CodeCount"/> populated
/// from the actual number of loaded entries.
/// </summary>
public static class CountryInfoRegistry
{
    /// <summary>
    /// All countries defined in <c>countries.json</c>, keyed by uppercase country ID.
    /// <see cref="CountryInfo.CodeCount"/> reflects the static JSON value (0 for countries
    /// with no embedded CSV). For the live count after loading a registry, use
    /// <see cref="ZipPostRegistry"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, CountryInfo> All => CountryInfoSource.All;

    /// <summary>
    /// Returns the <see cref="CountryInfo"/> for <paramref name="countryId"/>,
    /// or <c>null</c> if the country is not defined in <c>countries.json</c>.
    /// </summary>
    /// <param name="countryId">
    /// ISO 3166-1 alpha-2 code, e.g. <c>"US"</c> or <c>"ca"</c> (normalised to uppercase).
    /// </param>
    public static CountryInfo? TryGet(string countryId) =>
        CountryInfoSource.TryGet(countryId);
}