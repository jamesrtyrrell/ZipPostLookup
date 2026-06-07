using ZipPostLookup.Core;
using ZipPostLookup.Sources;

namespace ZipPostLookup;

/// <summary>
/// Static convenience methods for US postal code lookups and factory methods for
/// creating multi-country or custom registries.
/// </summary>
/// <remarks>
/// All static methods delegate to <see cref="ZipPostRegistry.Default"/>, which is loaded
/// from the built-in US data and initialised lazily on first access.
/// </remarks>
public static class ZipPostLookup
{

    /// <summary>Returns the default entry for the given postal code, or <c>null</c> if not found.</summary>
    public static CodeEntry? GetByCode(string? code) =>
        ZipPostRegistry.Default.GetByCode(code);

    /// <summary>Attempts to retrieve the default entry for the given postal code.</summary>
    public static bool TryGetByCode(string code, out CodeEntry? entry) =>
        ZipPostRegistry.Default.TryGetByCode(code, out entry);

    /// <summary>Returns all entries for the given postal code.</summary>
    public static IReadOnlyList<CodeEntry> GetAllByCode(string code) =>
        ZipPostRegistry.Default.GetAllByCode(code);

    /// <summary>Returns the default entry for the given US zip code. Alias for <see cref="GetByCode"/>.</summary>
    public static CodeEntry? GetByZip(string zip) =>
        ZipPostRegistry.Default.GetByCode(zip);

    /// <summary>Attempts to retrieve the default entry for the given US zip code.</summary>
    public static bool TryGetByZip(string zip, out CodeEntry? entry) =>
        ZipPostRegistry.Default.TryGetByCode(zip, out entry);

    /// <summary>Returns all entries for the given US zip code.</summary>
    public static IReadOnlyList<CodeEntry> GetAllByZip(string zip) =>
        ZipPostRegistry.Default.GetAllByCode(zip);

    /// <summary>
    /// Returns the default entry for the given US zip code, or the nearest numerically
    /// adjacent zip if no exact match exists.
    /// </summary>
    public static CodeEntry? GetClosest(string zip) =>
        ZipPostRegistry.Default.GetClosest(zip);

    /// <summary>Returns all entries whose place name matches the given value (case-insensitive).</summary>
    public static IReadOnlyList<CodeEntry> GetByName(string name) =>
        ZipPostRegistry.Default.GetByName(name);

    /// <summary>Returns all entries whose place name matches. Alias for <see cref="GetByName"/>.</summary>
    public static IReadOnlyList<CodeEntry> GetByCity(string city) =>
        ZipPostRegistry.Default.GetByCity(city);

    /// <summary>Returns all entries for a given admin level 1 code, e.g. <c>"NY"</c>.</summary>
    public static IReadOnlyList<CodeEntry> GetByState(string admin1Code) =>
        ZipPostRegistry.Default.GetByState(admin1Code);

    /// <summary>Returns all entries for a given admin level 1 name, e.g. <c>"New York"</c>.</summary>
    public static IReadOnlyList<CodeEntry> GetByStateName(string admin1Name) =>
        ZipPostRegistry.Default.GetByStateName(admin1Name);

    /// <summary>
    /// Returns all entries for a given admin value and level name.
    /// </summary>
    /// <example>
    /// <code>
    /// ZipPostLookup.GetByAdmin("California", "State");
    /// </code>
    /// </example>
    public static IReadOnlyList<CodeEntry> GetByAdmin(string value, string levelName) =>
        ZipPostRegistry.Default.GetByAdmin(value, levelName);

    /// <summary>Returns all entries for the given IANA timezone identifier.</summary>
    public static IReadOnlyList<CodeEntry> GetByTimeZone(string timezone) =>
        ZipPostRegistry.Default.GetByTimeZone(timezone);

    /// <summary>Returns a random entry from the default US registry.</summary>
    public static CodeEntry GetRandom() =>
        ZipPostRegistry.Default.GetRandom();

    /// <summary>Returns a random entry using the provided <see cref="Random"/> instance.</summary>
    public static CodeEntry GetRandom(Random random) =>
        ZipPostRegistry.Default.GetRandom(random);

    /// <summary>Returns all entries in the default US registry, sorted by code.</summary>
    public static IReadOnlyList<CodeEntry> GetAll() =>
        ZipPostRegistry.Default.GetAll();

    /// <summary>
    /// Looks up multiple US postal codes in a single pass.
    /// </summary>
    /// <remarks>
    /// <b>Performance note:</b> <c>GetBatch</c> is slower than a sequential loop for
    /// unique inputs (~1.4× at 100 codes) due to <c>Dictionary</c> construction overhead.
    /// It is faster than sequential when <b>duplicates are expected</b> — deduplication
    /// via <see cref="CodeBatch"/> means 200 codes with 50% duplicates resolves in
    /// 100 lookups rather than 200 (~33% faster). Build the batch once and reuse it.
    /// See <c>ZipPostvsZip2CityBenchmark.MD</c> for full batch benchmark results.
    /// </remarks>
    /// <param name="batch">
    /// A pre-built <see cref="CodeBatch"/> of deduplicated codes.
    /// </param>
    /// <returns>
    /// A read-only dictionary mapping each input code to its default entry, or
    /// <c>null</c> for codes not found.
    /// </returns>
    public static IReadOnlyDictionary<string, CodeEntry?> GetBatch(CodeBatch batch) =>
        ZipPostRegistry.Default.GetBatch(batch);

    /// <summary>
    /// Looks up multiple US postal codes in a single pass.
    /// Convenience overload — duplicates are silently ignored.
    /// Constructs a <see cref="CodeBatch"/> internally — prefer the
    /// <see cref="GetBatch(CodeBatch)"/> overload and reuse a pre-built batch
    /// when calling frequently.
    /// </summary>
    public static IReadOnlyDictionary<string, CodeEntry?> GetBatch(IEnumerable<string> codes) =>
        ZipPostRegistry.Default.GetBatch(codes);
    
    /// <summary>
    /// Creates a registry from one or more country codes using their built-in embedded data.
    /// </summary>
    /// <example>
    /// <code>
    /// var ca = ZipPostLookup.CreateRegistry(CountryCode.CA);
    /// var na = ZipPostLookup.CreateRegistry(CountryCode.US, CountryCode.CA);
    /// var us = ZipPostLookup.CreateRegistry("us");
    /// </code>
    /// </example>
    public static ZipPostRegistry CreateRegistry(params CountryCode[] countries) =>
        new ZipPostRegistry(countries);

    /// <summary>
    /// Returns the <see cref="CountryInfo"/> for the given country code,
    /// or <c>null</c> if the country is not defined in the embedded data.
    /// </summary>
    public static CountryInfo? GetCountryInfo(CountryCode country) =>
        CountryInfoRegistry.TryGet(country);

    /// <summary>
    /// Creates a registry merging the built-in US data with one or more custom data sources.
    /// </summary>
    /// <example>
    /// <code>
    /// var registry = ZipPostLookup.CreateCustomRegistry(new MyMilitaryZipSource());
    /// </code>
    /// </example>
    public static ZipPostRegistry CreateCustomRegistry(params ICodeDataSource[] additionalSources) =>
        new ZipPostRegistry(
            new ICodeDataSource[] { new BuiltInDataSource(CountryCode.US) }
                .Concat(additionalSources));
}