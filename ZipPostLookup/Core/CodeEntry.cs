namespace ZipPostLookup.Core;

/// <summary>
/// Represents a single postal code entry from the built-in registry.
/// </summary>
/// <remarks>
/// <para>
/// Each entry maps a postal code to a place name, IANA timezone, optional coordinates,
/// and one or more administrative subdivision levels (state, province, county, etc.).
/// </para>
/// <para>
/// Some postal codes have multiple entries — one per acceptable place name — with exactly
/// one marked as the default (see <see cref="IsDefault"/>).
/// </para>
/// <para>
/// <see cref="CodeEntry"/> is an immutable record. All properties are set at construction
/// and cannot be changed after loading.
/// </para>
/// <para>
/// Postal code formats vary by country:
/// <list type="bullet">
///   <item><description>US: 5-digit numeric, e.g. <c>"10001"</c></description></item>
///   <item><description>CA: 6-character alphanumeric, e.g. <c>"M5V3L9"</c></description></item>
///   <item><description>MX: 5-digit numeric, e.g. <c>"06600"</c></description></item>
/// </list>
/// </para>
/// </remarks>
/// <param name="ZpCode">
/// The postal code. Format depends on country.
/// </param>
/// <param name="PlaceName">
/// The primary place name — city, locality, colonia, or community — associated with this
/// postal code, e.g. <c>"New York"</c>, <c>"Toronto"</c>, <c>"Zona Centro"</c>.
/// </param>
/// <param name="Timezone">
/// The IANA timezone identifier for this postal code,
/// e.g. <c>"America/New_York"</c> or <c>"America/Toronto"</c>.
/// </param>
/// <param name="IsDefault">
/// <c>true</c> if this is the primary entry for the postal code when multiple place names
/// exist; otherwise <c>false</c>. <see cref="ZipPostRegistry.GetByCode"/> always returns the
/// default entry.
/// </param>
/// <param name="Admins">
/// Administrative subdivision levels, ordered from largest to smallest as defined in the
/// country schema. Empty array if no administrative data is available.
/// Use <see cref="Admin1"/>, <see cref="Admin1Code"/> etc. for convenient access to the
/// most common levels, or <see cref="GetAdmin(string)"/> to look up by level name.
/// </param>
public sealed record CodeEntry(
    string ZpCode,
    string PlaceName,
    string Timezone,
    bool IsDefault,
    AdminLevel[] Admins
)
{
    // -------------------------------------------------------------------------
    // Convenience properties for common admin levels
    // -------------------------------------------------------------------------

    /// <summary>
    /// The value of the first administrative subdivision level (largest division),
    /// e.g. state name, province name, or estado name. <c>null</c> if not available.
    /// </summary>
    public string? Admin1 => Admins.Length > 0 ? Admins[0].Value : null;

    /// <summary>
    /// The code of the first administrative subdivision level,
    /// e.g. <c>"NY"</c>, <c>"ON"</c>, <c>"JAL"</c>. <c>null</c> if not available.
    /// </summary>
    public string? Admin1Code => Admins.Length > 0 ? Admins[0].Code : null;

    /// <summary>
    /// The value of the second administrative subdivision level,
    /// e.g. county name, municipio name. <c>null</c> if not available.
    /// </summary>
    public string? Admin2 => Admins.Length > 1 ? Admins[1].Value : null;

    /// <summary>
    /// The code of the second administrative subdivision level. <c>null</c> if not available.
    /// </summary>
    public string? Admin2Code => Admins.Length > 1 ? Admins[1].Code : null;

    /// <summary>
    /// The value of the third administrative subdivision level. <c>null</c> if not available.
    /// </summary>
    public string? Admin3 => Admins.Length > 2 ? Admins[2].Value : null;

    /// <summary>
    /// The code of the third administrative subdivision level. <c>null</c> if not available.
    /// </summary>
    public string? Admin3Code => Admins.Length > 2 ? Admins[2].Code : null;

    // -------------------------------------------------------------------------
    // Admin lookup by level name
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the <see cref="AdminLevel"/> whose <see cref="AdminLevel.LevelName"/>
    /// matches <paramref name="levelName"/> (case-insensitive), or <c>null</c> if not found.
    /// </summary>
    /// <param name="levelName">
    /// The level name to search for, e.g. <c>"State"</c>, <c>"Province"</c>,
    /// <c>"Estado"</c>, <c>"Taluka"</c>. Aliases defined in the country schema
    /// are also matched.
    /// </param>
    /// <returns>
    /// The matching <see cref="AdminLevel"/>, or <c>null</c> if no level with that name exists.
    /// </returns>
    public AdminLevel? GetAdmin(string levelName)
    {
        foreach (var admin in Admins)
        {
            if (string.Equals(admin.LevelName, levelName, StringComparison.OrdinalIgnoreCase))
            {
                return admin;
            }
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the current local date and time at this postal code's location.
    /// </summary>
    /// <returns>
    /// A <see cref="DateTimeOffset"/> representing the current UTC time converted to this
    /// entry's IANA timezone.
    /// </returns>
    /// <exception cref="TimeZoneNotFoundException">
    /// Thrown if the IANA timezone identifier is not recognised by the current platform.
    /// </exception>
    public DateTimeOffset GetLocalTime() =>
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(Timezone));

    /// <summary>
    /// Returns a human-readable summary, e.g.
    /// <c>"10001 New York, New York (America/New_York)"</c>.
    /// </summary>
    public override string ToString() =>
        Admin1 is { Length: > 0 } a1
            ? $"{ZpCode} {PlaceName}, {a1} ({Timezone})"
            : $"{ZpCode} {PlaceName} ({Timezone})";
}