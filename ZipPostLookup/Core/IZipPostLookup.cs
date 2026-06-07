namespace ZipPostLookup.Core;

/// <summary>
/// Defines the lookup capabilities of a postal code registry.
/// </summary>
/// <remarks>
/// <para>
/// Program against <see cref="IZipPostLookup"/> rather than <see cref="ZipPostRegistry"/> directly
/// to keep your code loosely coupled, testable, and mockable.
/// </para>
/// <para>
/// All lookup methods are case-insensitive unless otherwise noted, and return empty collections
/// rather than <c>null</c> when no results are found.
/// </para>
/// </remarks>
public interface IZipPostLookup
{
    // -------------------------------------------------------------------------
    // Code lookups — exact match
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the default entry for the given postal code, or <c>null</c> if not found or <paramref name="code"/> is <c>null</c>.
    /// </summary>
    /// <param name="code">The postal code to look up (case-insensitive). <c>null</c> returns <c>null</c>.</param>
    /// <returns>The default <see cref="CodeEntry"/>, or <c>null</c> if not found.</returns>
    CodeEntry? GetByCode(string? code);

    /// <summary>
    /// Attempts to retrieve the default entry for the given postal code.
    /// </summary>
    /// <param name="code">The postal code to look up (case-insensitive). <c>null</c> returns <c>false</c>.</param>
    /// <param name="entry">The matched entry, or <c>null</c> if not found.</param>
    /// <returns><c>true</c> if found; otherwise <c>false</c>.</returns>
    bool TryGetByCode(string? code, out CodeEntry? entry);

    /// <summary>
    /// Returns all entries for the given postal code (some codes map to multiple place names).
    /// </summary>
    /// <param name="code">The postal code to look up (case-insensitive).</param>
    /// <returns>All matching entries, default entry first. Empty list if not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="code"/> is <c>null</c>.</exception>
    IReadOnlyList<CodeEntry> GetAllByCode(string code);

    // -------------------------------------------------------------------------
    // US convenience aliases — map to GetByCode
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the default entry for the given postal code, or <c>null</c> if not found.
    /// Alias for <see cref="GetByCode"/> — familiar name for US developers.
    /// </summary>
    CodeEntry? GetByZip(string zip);

    /// <summary>Alias for <see cref="TryGetByCode"/>.</summary>
    bool TryGetByZip(string zip, out CodeEntry? entry);

    /// <summary>Alias for <see cref="GetAllByCode"/>.</summary>
    IReadOnlyList<CodeEntry> GetAllByZip(string zip);

    // -------------------------------------------------------------------------
    // Closest match — US only
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the default entry for the given US zip code, or the nearest numerically
    /// adjacent zip code if no exact match exists.
    /// </summary>
    /// <param name="zip">A 5-digit numeric US zip code string, e.g. <c>"10001"</c>.</param>
    /// <returns>The exact or nearest matching <see cref="CodeEntry"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zip"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="zip"/> is not a 5-digit numeric string.</exception>
    CodeEntry? GetClosest(string zip);

    // -------------------------------------------------------------------------
    // Reverse lookups
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all entries whose place name matches the given value (case-insensitive).
    /// </summary>
    /// <param name="name">The place name to search for, e.g. <c>"New York"</c>, <c>"Toronto"</c>.</param>
    /// <returns>All matching entries, or an empty list if none found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is <c>null</c>.</exception>
    IReadOnlyList<CodeEntry> GetByName(string name);

    /// <summary>
    /// Returns all entries whose place name matches the given value (case-insensitive).
    /// Alias for <see cref="GetByName"/> — familiar name for US developers.
    /// </summary>
    IReadOnlyList<CodeEntry> GetByCity(string city);

    /// <summary>
    /// Returns all entries for a given first-level administrative subdivision code
    /// (case-insensitive), e.g. <c>"NY"</c>, <c>"ON"</c>, <c>"JAL"</c>.
    /// </summary>
    /// <param name="admin1Code">The admin level 1 code to search for.</param>
    /// <returns>All matching entries, or an empty list if none found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="admin1Code"/> is <c>null</c>.</exception>
    IReadOnlyList<CodeEntry> GetByState(string admin1Code);

    /// <summary>
    /// Returns all entries for a given first-level administrative subdivision name
    /// (case-insensitive), e.g. <c>"New York"</c>, <c>"Ontario"</c>, <c>"Jalisco"</c>.
    /// </summary>
    /// <param name="admin1Name">The admin level 1 name to search for.</param>
    /// <returns>All matching entries, or an empty list if none found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="admin1Name"/> is <c>null</c>.</exception>
    IReadOnlyList<CodeEntry> GetByStateName(string admin1Name);

    /// <summary>
    /// Returns all entries for the given admin level value and level name.
    /// The level name is matched against the <see cref="AdminLevel.LevelName"/> and any
    /// aliases defined in the country schema (case-insensitive).
    /// </summary>
    /// <param name="value">
    /// The admin division value or code to search for,
    /// e.g. <c>"Ontario"</c>, <c>"ON"</c>, <c>"Jalisco"</c>, <c>"JAL"</c>.
    /// </param>
    /// <param name="levelName">
    /// The level name to search within, e.g. <c>"State"</c>, <c>"Province"</c>,
    /// <c>"Territory"</c>, <c>"Estado"</c>, <c>"Taluka"</c>.
    /// Aliases from the country schema are also matched.
    /// </param>
    /// <returns>All matching entries, or an empty list if none found.</returns>
    /// <example>
    /// <code>
    /// // All postal codes in Ontario (by level name)
    /// var entries = registry.GetByAdmin("Ontario", "Province");
    ///
    /// // Same result using an alias
    /// var entries = registry.GetByAdmin("ON", "Territory");
    ///
    /// // Mexico — by Estado
    /// var entries = registry.GetByAdmin("Jalisco", "Estado");
    /// </code>
    /// </example>
    IReadOnlyList<CodeEntry> GetByAdmin(string value, string levelName);

    /// <summary>
    /// Returns all entries for the given IANA timezone identifier (case-insensitive).
    /// </summary>
    /// <param name="timezone">An IANA timezone identifier, e.g. <c>"America/New_York"</c>.</param>
    /// <returns>All matching entries, or an empty list if none found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timezone"/> is <c>null</c>.</exception>
    IReadOnlyList<CodeEntry> GetByTimeZone(string timezone);

    // -------------------------------------------------------------------------
    // Random and enumeration
    // -------------------------------------------------------------------------

    /// <summary>Returns a random entry using a thread-safe shared random instance.</summary>
    CodeEntry GetRandom();

    /// <summary>Returns a random entry using the provided <see cref="Random"/> instance.</summary>
    CodeEntry GetRandom(Random random);

    /// <summary>Returns all entries sorted by postal code.</summary>
    IReadOnlyList<CodeEntry> GetAll();

    // -------------------------------------------------------------------------
    // Batch lookups
    // -------------------------------------------------------------------------

    /// <summary>
    /// Looks up multiple postal codes in a single pass, returning a dictionary
    /// mapping each input code to its default <see cref="CodeEntry"/> (or <c>null</c>
    /// if not found).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Performance note:</b> <c>GetBatch</c> is <b>slower than a sequential loop</b>
    /// for unique inputs — the <c>Dictionary</c> construction overhead outweighs any
    /// per-lookup savings (~1.4× slower at 100 codes). Use a plain loop with
    /// <see cref="GetByCode"/> for clean unique input sets.
    /// </para>
    /// <para>
    /// <c>GetBatch</c> wins when <b>duplicates are expected</b> — it deduplicates via
    /// <see cref="CodeBatch"/> before looking up, so 200 codes with 50% duplicates
    /// resolves in 100 lookups rather than 200 (~33% faster than sequential in that case).
    /// </para>
    /// <para>
    /// See <c>ZipPostvsZip2CityBenchmark.MD</c> for full batch benchmark results.
    /// </para>
    /// </remarks>
    /// <param name="batch">
    /// A <see cref="CodeBatch"/> of deduplicated codes to look up.
    /// Build the batch once and reuse it — construction costs ~1,066 ns per 100 codes.
    /// The batch is sealed on first iteration — create a new instance to reuse.
    /// </param>
    /// <returns>
    /// A read-only dictionary keyed by the original input code (case-preserved).
    /// Values are <c>null</c> for codes not found in the registry.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="batch"/> is <c>null</c>.</exception>
    IReadOnlyDictionary<string, CodeEntry?> GetBatch(CodeBatch batch);

    /// <summary>
    /// Looks up multiple postal codes in a single pass.
    /// Convenience overload — duplicates in <paramref name="codes"/> are silently ignored.
    /// </summary>
    /// <remarks>
    /// Constructs a <see cref="CodeBatch"/> internally on every call — prefer the
    /// <see cref="GetBatch(CodeBatch)"/> overload and reuse a pre-built batch when
    /// calling frequently. See performance notes on <see cref="GetBatch(CodeBatch)"/>.
    /// </remarks>
    /// <param name="codes">The postal codes to look up.</param>
    /// <returns>
    /// A read-only dictionary keyed by the original input code (case-preserved).
    /// Values are <c>null</c> for codes not found in the registry.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="codes"/> is <c>null</c>.</exception>
    IReadOnlyDictionary<string, CodeEntry?> GetBatch(IEnumerable<string> codes);
}